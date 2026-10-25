using PclLocalTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class NetworkTests {
    private static byte[] Json(object value) => Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
    private static string Sha(byte[] bytes) { using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private sealed class Handler : HttpMessageHandler {
        private readonly Dictionary<string, byte[]> bodies = new Dictionary<string, byte[]>();
        public bool ClientUsedRange, CorruptVersion, OversizedManifest;
        public int Calls;
        public Handler() {
            var asset = Encoding.UTF8.GetBytes("synthetic asset"); var assetHash = Sha(asset);
            var library = Encoding.UTF8.GetBytes("synthetic library");
            var index = Json(new { objects = new Dictionary<string, object> { { "fixture.txt", new { hash = assetHash, size = asset.Length } } } });
            var version = Json(new {
                downloads = new { client = new { url = "https://piston-data.mojang.com/fixture/client.jar", size = 1048576 } },
                assetIndex = new { url = "https://piston-meta.mojang.com/fixture/assets.json", sha1 = Sha(index) },
                libraries = new[] { new { downloads = new { artifact = new { url = "https://libraries.minecraft.net/fixture.jar", size = library.Length, sha1 = Sha(library) } } } }
            });
            var manifest = Json(new { latest = new { release = "fixture" }, versions = new[] { new { id = "fixture", url = "https://piston-meta.mojang.com/fixture/version.json", sha1 = Sha(version) } } });
            bodies["https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"] = manifest;
            bodies["https://piston-meta.mojang.com/fixture/version.json"] = version;
            bodies["https://piston-meta.mojang.com/fixture/assets.json"] = index;
            bodies["https://piston-data.mojang.com/fixture/client.jar"] = new byte[] { 1 };
            bodies["https://resources.download.minecraft.net/" + assetHash.Substring(0, 2) + "/" + assetHash] = asset;
            bodies["https://libraries.minecraft.net/fixture.jar"] = library;
            bodies["https://meta.fabricmc.net/v2/versions/game"] = Encoding.UTF8.GetBytes("[]");
            bodies["https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"] = manifest;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            var key = request.RequestUri.AbsoluteUri; var data = bodies[key];
            if (CorruptVersion && key.EndsWith("version.json")) data = Encoding.UTF8.GetBytes("corrupted fixture");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(data) };
            if (key.EndsWith("client.jar")) {
                ClientUsedRange = request.Headers.Range?.Ranges.Single().From == 0 && request.Headers.Range?.Ranges.Single().To == 0;
                response.StatusCode = HttpStatusCode.PartialContent; response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0, 1048576);
            }
            if (OversizedManifest && key == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json") response.Content.Headers.ContentLength = 9 * 1024 * 1024;
            return Task.FromResult(response);
        }
    }
    public static List<Tuple<bool, string>> Run() {
        var results = new List<Tuple<bool, string>>();
        var valid = new Handler(); var rows = DownloadDiagnostics.Run(CancellationToken.None, null, valid).GetAwaiter().GetResult();
        results.Add(Tuple.Create(rows.Count == 8 && rows.All(r => !r.Status.StartsWith("失败")), "mock HTTP metadata chain and all eight diagnostic stages succeed"));
        results.Add(Tuple.Create(valid.ClientUsedRange && rows.Single(r => r.Name == "游戏客户端地址").Status.Contains("未核验完整文件"), "client probe requests one byte and reports verification limits"));
        results.Add(Tuple.Create(rows.Count(r => r.Status.Contains("SHA-1 匹配")) == 4, "version JSON, asset index, sampled asset and library SHA-1 verified"));
        var corrupt = new Handler { CorruptVersion = true }; var bad = DownloadDiagnostics.Run(CancellationToken.None, null, corrupt).GetAwaiter().GetResult();
        results.Add(Tuple.Create(bad.Any(r => r.Name.StartsWith("版本元数据") && r.Status.Contains("校验失败")) && bad.Any(r => r.Name.StartsWith("Fabric")), "metadata hash mismatch reported while independent endpoints still checked"));
        var big = DownloadDiagnostics.Run(CancellationToken.None, null, new Handler { OversizedManifest = true }).GetAwaiter().GetResult();
        results.Add(Tuple.Create(big[0].Status.Contains("上限") && big.Count == 3, "oversized metadata rejected before dependent requests"));
        var cts = new CancellationTokenSource(); cts.Cancel(); var cancelled = new Handler();
        bool rejected = false;
        try { DownloadDiagnostics.Run(cts.Token, null, cancelled).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
        results.Add(Tuple.Create(rejected && cancelled.Calls == 0, "cancelled diagnostic makes no HTTP requests"));
        return results;
    }
}
