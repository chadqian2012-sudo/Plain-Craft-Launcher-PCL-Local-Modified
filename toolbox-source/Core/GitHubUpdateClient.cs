using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PclLocalTools {
    public sealed class GitHubUpdateClient : IDisposable {
        private readonly HttpClient client;
        public GitHubUpdateClient(HttpMessageHandler? handler = null) {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Local-Updater/" + UpdateProtocol.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        public void Dispose() => client.Dispose();
        private static void CheckHost(Uri uri) {
            if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("更新请求跳转到了不受支持的地址。");
        }
        private async Task<HttpResponseMessage> Response(string url, CancellationToken cancellation) {
            var uri = new Uri(url);
            for (int i = 0; i < 6; i++) {
                CheckHost(uri);
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri)) {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399 && response.Headers.Location != null) {
                        uri = new Uri(uri, response.Headers.Location); response.Dispose(); continue;
                    }
                    if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new IOException(code == 403 || code == 429 ? "GitHub 请求受限，请稍后手动重试。" : "检查更新失败：HTTP " + code); }
                    // Verify the final URI too if a test/custom handler performed redirection.
                    if (response.RequestMessage?.RequestUri != null) CheckHost(response.RequestMessage.RequestUri);
                    return response;
                }
            }
            throw new IOException("更新服务跳转次数过多。");
        }
        private async Task<byte[]> Bytes(string url, int limit, CancellationToken cancellation) {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (var response = await Response(url, timeout.Token).ConfigureAwait(false)) {
                    if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("更新元数据过大。");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream()) {
                        var buffer = new byte[8192]; int n;
                        while ((n = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0) { if (output.Length + n > limit) throw new InvalidDataException("更新元数据超过读取上限。"); output.Write(buffer, 0, n); }
                        return output.ToArray();
                    }
                }
            }
        }
        public async Task<UpdateRelease?> Check(string currentVersion, CancellationToken cancellation) {
            try { return UpdateRelease.Parse(await Bytes(UpdateProtocol.Api, 2 * 1024 * 1024, cancellation).ConfigureAwait(false), currentVersion); }
            catch (IOException ex) when (ex.Message.Contains("HTTP 404")) { return null; }
        }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static void ApiHash(UpdateAsset asset, string actual) {
            if (asset.Digest.Length == 0) return;
            if (!asset.Digest.Equals("sha256:" + actual, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GitHub 附件 SHA-256 与实际文件不一致。");
        }
        public async Task<UpdateManifest> Manifest(UpdateRelease release, CancellationToken cancellation) {
            var asset = release.Assets.Single(a => a.Name == "PCL-update.json");
            var bytes = await Bytes(asset.Url, 256 * 1024, cancellation).ConfigureAwait(false);
            if (asset.Bytes != bytes.Length) throw new InvalidDataException("更新清单长度不匹配。");
            ApiHash(asset, Hash(bytes));
            var manifest = UpdateProtocol.Json().Deserialize<UpdateManifest>(System.Text.Encoding.UTF8.GetString(bytes));
            if (manifest == null) throw new InvalidDataException("更新清单为空。"); manifest.Validate(release.Version);
            var archive = release.Assets.SingleOrDefault(a => a.Name == manifest.ArchiveName) ?? throw new InvalidDataException("发行版缺少更新清单指定的程序 ZIP。");
            if (archive.Bytes <= 0 || archive.Bytes > 128L * 1024 * 1024) throw new InvalidDataException("程序更新包大小不受支持。");
            ApiHash(archive, manifest.ArchiveSha256); return manifest;
        }
        public async Task<string> Download(UpdateRelease release, UpdateManifest manifest, string transactionDirectory, CancellationToken cancellation, IProgress<string>? progress = null) {
            manifest.Validate(release.Version); FileSafety.NoLinks(transactionDirectory); Directory.CreateDirectory(transactionDirectory);
            var asset = release.Assets.Single(a => a.Name == manifest.ArchiveName);
            var partial = Path.Combine(transactionDirectory, "download.zip.partial"); var final = Path.Combine(transactionDirectory, "download.zip");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                timeout.CancelAfter(TimeSpan.FromMinutes(4));
                using (var response = await Response(asset.Url, timeout.Token).ConfigureAwait(false)) {
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != asset.Bytes) throw new InvalidDataException("程序 ZIP 的响应长度不匹配。");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true)) {
                        var buffer = new byte[128 * 1024]; long total = 0; int n;
                        while ((n = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0) {
                            total += n; if (total > asset.Bytes || total > 128L * 1024 * 1024) throw new InvalidDataException("更新 ZIP 超过声明大小。");
                            await output.WriteAsync(buffer, 0, n, timeout.Token).ConfigureAwait(false); progress?.Report("下载更新：" + (total * 100 / asset.Bytes) + "%");
                        }
                        if (total != asset.Bytes) throw new InvalidDataException("程序 ZIP 下载不完整。");
                    }
                }
            }
            progress?.Report("核对更新包 SHA-256…");
            string hash; using (var verify = File.OpenRead(partial)) hash = FileSafety.Hash(verify, cancellation);
            if (!hash.Equals(manifest.ArchiveSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("程序 ZIP 校验失败，未安装更新。");
            ApiHash(asset, hash); cancellation.ThrowIfCancellationRequested(); File.Move(partial, final); return final;
        }
    }
}
