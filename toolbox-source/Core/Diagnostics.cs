using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PclLocalTools {
    public static class LogDiagnostics {
        public static string Advice(string text) {
            var rules = new[] {
                Tuple.Create("UnsupportedClassVersionError|class file version|compiled by a more recent version", "Java 版本可能过低。查看版本 JSON 的 javaVersion 或模组要求，在版本设置中选择匹配的 Java。"),
                Tuple.Create("OutOfMemoryError|Could not reserve enough space|Invalid maximum heap", "Java 内存不足或分配参数无效。核对系统剩余内存、64 位 Java 和内存上限；不要直接把分配值设为全部物理内存。"),
                Tuple.Create("Incompatible mod set|Mod resolution encountered|requires.*version|Missing.*dependenc|mandatory dependenc", "模组依赖或版本可能不匹配。先查看错误中列出的模组、所需版本和加载器，不要批量删除所有模组。"),
                Tuple.Create("ClassNotFoundException|Could not find or load main class|NoClassDefFoundError", "游戏入口或依赖库可能缺失，也可能是模组依赖不完整。保留存档后检查安装完整性和日志中的具体类名。"),
                Tuple.Create("checksum|hash mismatch|SHA1.*(fail|error)|校验失败|哈希.*不匹配", "下载文件的校验值不匹配。使用原下载任务的重试入口重新获取该文件；多次失败时再切换下载源。"),
                Tuple.Create("UnauthorizedAccessException|AccessDenied|Permission denied|拒绝访问|无法写入", "目录或文件无法写入。检查文件占用、磁盘剩余空间与目录权限，优先使用自己可写的游戏目录。"),
                Tuple.Create("HTTP.*(403|429)|Forbidden|Too Many Requests", "下载服务拒绝请求或限流。稍后重试，并用下载诊断页核对具体地址；避免连续重复请求。"),
                Tuple.Create("HTTP.*404|Not Found", "目标下载文件或版本地址不存在。先刷新版本列表，仍失败时记录具体 URL 与版本号。"),
                Tuple.Create("timed? ?out|TimeoutException|无法解析|NameResolution|No such host|SSL|TLS|连接.*失败|超时", "网络、DNS 或 TLS 连接可能失败。运行下载诊断，分别检查元数据、资源和依赖库，勿仅凭网站首页可打开就判断正常。")
            };
            return string.Join(Environment.NewLine + Environment.NewLine, rules.Where(r => Regex.IsMatch(text, r.Item1, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(r => "• " + r.Item2));
        }
        public static string? ExplainError(string? message) {
            if (message == null || message.Length == 0) return message;
            try { var advice = Advice(message); return advice.Length == 0 ? message : message + Environment.NewLine + advice; }
            catch { return message; }
        }
        public static string ReadTail(string path, int maxBytes = 2 * 1024 * 1024) {
            FileSafety.NoLinks(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                var remaining = Math.Min(stream.Length, maxBytes); stream.Seek(-remaining, SeekOrigin.End);
                using (var output = new MemoryStream()) {
                    var buffer = new byte[8192]; int read;
                    while (remaining > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0) { output.Write(buffer, 0, read); remaining -= read; }
                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }
        public static string Redact(string text, IEnumerable<string>? gameRoots = null) {
            if (gameRoots != null) foreach (var path in gameRoots.Where(p => !string.IsNullOrWhiteSpace(p)).OrderByDescending(p => p.Length)) text = Regex.Replace(text, Regex.Escape(path.TrimEnd('\\', '/')), "$GAME_ROOT", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?i)\bBearer\s+[^\s""'<>]+", "Bearer [REDACTED]");
            text = Regex.Replace(text, @"(?i)(""(?:access[_-]?token|refresh[_-]?token|client[_-]?token|authorization|password|passwd|session|xuid|uuid|username|login[_-]?token)""\s*:\s*"")(?:\\.|[^""\\])*("")", "$1[REDACTED]$2");
            text = Regex.Replace(text, @"(?i)((?:--)?(?:access[_-]?token|refresh[_-]?token|client[_-]?token|authorization|password|passwd|session|xuid|uuid|username|login[_-]?token)\b[""']?\s*(?:[:=]|\s)\s*[""']?)([^\s""'&,<>}]+)", "$1[REDACTED]");
            text = Regex.Replace(text, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", "[JWT REDACTED]");
            text = Regex.Replace(text, @"(?i)[A-Z]:[\\/]Users[\\/][^\\/\s]+", "[USER_HOME]");
            text = Regex.Replace(text, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[EMAIL REDACTED]");
            return text;
        }
        public static string Report(string log, IEnumerable<string>? roots = null) {
            var advice = Advice(log);
            var excerpt = log.Length > 80000 ? log.Substring(log.Length - 80000) : log;
            return "PCL 本地工具箱 2.99.2 诊断报告\r\n时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                "\r\n以下结论来自日志关键字匹配，需要结合原始错误确认。\r\n\r\n" +
                (advice.Length == 0 ? "未匹配到已知故障类型，请查看下面的日志摘录。" : advice) +
                "\r\n\r\n脱敏日志摘录（最多 80000 字符；分享前请再检查）：\r\n" + Redact(excerpt, roots);
        }
    }
    public sealed class NetworkResult {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Status { get; set; } = "";
        public long Milliseconds { get; set; }
    }
    public static class DownloadDiagnostics {
        private const int MetadataLimit = 8 * 1024 * 1024;
        private static Dictionary<string, object> Object(object data) => (Dictionary<string, object>)data;
        private static string Text(Dictionary<string, object> data, string key) => Convert.ToString(data[key], System.Globalization.CultureInfo.InvariantCulture)!;
        private static Dictionary<string, object> Parse(byte[] bytes) => Object(new JavaScriptSerializer { MaxJsonLength = MetadataLimit }.DeserializeObject(Encoding.UTF8.GetString(bytes)));
        private static async Task<byte[]> Fetch(HttpClient client, string url, int limit, CancellationToken cancellation, bool probe = false) {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new IOException("下载诊断只访问 HTTPS 地址。");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(18)); cancellation = deadline.Token;
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri)) {
                if (probe) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage.RequestUri.Scheme != Uri.UriSchemeHttps) throw new IOException("服务重定向到了非 HTTPS 地址。");
                    if (!probe && response.Content.Headers.ContentLength > limit) throw new IOException("响应超过诊断下载上限。");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream()) {
                        var buffer = new byte[8192]; int read;
                        while ((read = await stream.ReadAsync(buffer, 0, probe ? 1 : buffer.Length, cancellation).ConfigureAwait(false)) > 0) {
                            if (output.Length + read > limit) throw new IOException("响应超过诊断下载上限。");
                            output.Write(buffer, 0, read); if (probe) break;
                        }
                        if (output.Length == 0) throw new IOException("服务返回空响应。"); return output.ToArray();
                    }
                }
            }
            }
        }
        private static void Sha1(byte[] bytes, string expected) {
            using (var sha = SHA1.Create()) if (!BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("官方元数据 SHA-1 校验失败。");
        }
        public static async Task<List<NetworkResult>> Run(CancellationToken cancellation, IProgress<NetworkResult>? progress = null, HttpMessageHandler? handlerOverride = null) {
            var results = new List<NetworkResult>();
            using (var handler = handlerOverride ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) }) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-LocalTools/2.99.2");
                async Task<byte[]?> Check(string name, string url, int limit, string? expected = null, bool probe = false) {
                    cancellation.ThrowIfCancellationRequested(); var timer = Stopwatch.StartNew(); var row = new NetworkResult { Name = name, Url = url };
                    byte[]? data = null;
                    try {
                        data = await Fetch(client, url, limit, cancellation, probe).ConfigureAwait(false);
                        if (expected != null) Sha1(data, expected);
                        row.Status = probe ? "可达（仅探测 1 字节，未核验完整文件）" : expected != null ? "成功，SHA-1 匹配（" + data.Length + " 字节）" : "可达（" + data.Length + " 字节）";
                    } catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellation.IsCancellationRequested) { row.Status = "失败：" + ex.Message; data = null; }
                    row.Milliseconds = timer.ElapsedMilliseconds; results.Add(row); progress?.Report(row); return data;
                }
                var manifestBytes = await Check("官方版本清单", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", MetadataLimit).ConfigureAwait(false);
                if (manifestBytes != null) {
                    try {
                        var manifest = Parse(manifestBytes); var latest = Text(Object(manifest["latest"]), "release");
                        var version = ((object[])manifest["versions"]).Select(Object).First(v => Text(v, "id") == latest);
                        var versionBytes = await Check("版本元数据：" + latest, Text(version, "url"), MetadataLimit, version.ContainsKey("sha1") ? Text(version, "sha1") : null).ConfigureAwait(false);
                        if (versionBytes != null) {
                            var info = Parse(versionBytes);
                            var clientInfo = Object(Object(info["downloads"])["client"]);
                            await Check("游戏客户端地址", Text(clientInfo, "url"), 1, null, true).ConfigureAwait(false);
                            var assets = Object(info["assetIndex"]);
                            var assetBytes = await Check("资源索引", Text(assets, "url"), MetadataLimit, Text(assets, "sha1")).ConfigureAwait(false);
                            if (assetBytes != null) {
                                var objects = Object(Parse(assetBytes)["objects"]).Values.Select(Object);
                                var sample = objects.FirstOrDefault(a => Convert.ToInt64(a["size"]) > 0 && Convert.ToInt64(a["size"]) <= 256 * 1024);
                                if (sample != null) {
                                    var hash = Text(sample, "hash");
                                    await Check("资源对象抽样", "https://resources.download.minecraft.net/" + hash.Substring(0, 2) + "/" + hash, 256 * 1024, hash).ConfigureAwait(false);
                                }
                            }
                            var library = ((object[])info["libraries"]).Select(Object).Where(l => l.ContainsKey("downloads")).Select(l => Object(l["downloads"]))
                                .Where(d => d.ContainsKey("artifact")).Select(d => Object(d["artifact"]))
                                .FirstOrDefault(d => d.ContainsKey("size") && Convert.ToInt64(d["size"]) > 0 && Convert.ToInt64(d["size"]) <= 512 * 1024);
                            if (library != null) await Check("依赖库抽样", Text(library, "url"), 512 * 1024, Text(library, "sha1")).ConfigureAwait(false);
                        }
                    } catch (Exception ex) when (!(ex is OperationCanceledException) || !cancellation.IsCancellationRequested) {
                        var row = new NetworkResult { Name = "元数据解析", Status = "失败：" + ex.Message }; results.Add(row); progress?.Report(row);
                    }
                }
                await Check("Fabric 版本列表", "https://meta.fabricmc.net/v2/versions/game", MetadataLimit).ConfigureAwait(false);
                await Check("BMCLAPI 版本清单", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json", MetadataLimit).ConfigureAwait(false);
            }
            return results;
        }
    }
}
