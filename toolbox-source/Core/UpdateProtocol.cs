using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PclLocalTools {
    public static class UpdateProtocol {
        public static readonly string Version = typeof(UpdateProtocol).Assembly.GetName().Version.ToString(3);
        public const string Repository = "chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified";
        public const string Api = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ReleaseBase = "https://github.com/" + Repository + "/releases/";
        public const string LauncherName = "Plain Craft Launcher (PCL) 本地修改版.exe";
        public const string HelperName = "PCL Update Helper.exe";
        public static readonly string[] PayloadNames = { LauncherName, "PclLocalTools.dll", "PCL Local Tools.exe", "PCL Local Tools.exe.config", HelperName, HelperName + ".config" };
        public static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 };
        public static System.Version ParseVersion(string value) {
            var text = value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value.Substring(1) : value;
            if (!Regex.IsMatch(text, @"^\d+\.\d+\.\d+(?:\.\d+)?$") || !System.Version.TryParse(text, out var v)) throw new InvalidDataException("版本号不是受支持的正式版本：" + value);
            return new System.Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }
        public static bool HashText(string? text) => text != null && Regex.IsMatch(text, "^[A-Fa-f0-9]{64}$");
        public static string Leaf(string name) {
            if (FileSafety.SafeRelative(name).Contains("/")) throw new InvalidDataException("更新只允许程序根目录文件。");
            return name;
        }
        public static bool AssetUrl(string url) {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal);
        }
        public static void Save<T>(string path, T value) {
            FileSafety.NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, Json().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        public static T Load<T>(string path, int maximum = 2 * 1024 * 1024) {
            FileSafety.NoLinks(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Length > maximum) throw new InvalidDataException("更新记录过大。");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Json().Deserialize<T>(reader.ReadToEnd());
            }
        }
    }
    public sealed class UpdateFile {
        public string Name { get; set; } = "";
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = "";
    }
    public sealed class UpdateManifest {
        public int Format { get; set; }
        public string Version { get; set; } = "";
        public string ArchiveName { get; set; } = "";
        public string ArchiveSha256 { get; set; } = "";
        public List<UpdateFile> Files { get; set; } = new List<UpdateFile>();
        public void Validate(string version) {
            if (Format != 1 || UpdateProtocol.ParseVersion(Version) != UpdateProtocol.ParseVersion(version)) throw new InvalidDataException("更新清单的版本与发行版不一致。");
            UpdateProtocol.Leaf(ArchiveName);
            if (!ArchiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !UpdateProtocol.HashText(ArchiveSha256)) throw new InvalidDataException("更新包信息无效。");
            if (Files == null || Files.Count != UpdateProtocol.PayloadNames.Length || !new HashSet<string>(Files.Select(f => f.Name), StringComparer.Ordinal).SetEquals(UpdateProtocol.PayloadNames)) throw new InvalidDataException("更新清单没有完整的程序文件，或包含了非程序文件。");
            foreach (var file in Files) {
                UpdateProtocol.Leaf(file.Name);
                if (file.Bytes <= 0 || file.Bytes > 32 * 1024 * 1024 || !UpdateProtocol.HashText(file.Sha256)) throw new InvalidDataException("更新文件大小或校验值无效。");
            }
        }
    }
    public sealed class UpdateAsset {
        public string Name = "", Url = "", Digest = ""; public long Bytes;
    }
    public sealed class UpdateRelease {
        public string Version = "", Name = "", Body = "", Page = "";
        public List<UpdateAsset> Assets = new List<UpdateAsset>();
        public bool SupportsInstall => Assets.Count(a => a.Name == "PCL-update.json") == 1;
        public static UpdateRelease? Parse(byte[] data, string current) {
            var json = (Dictionary<string, object>)UpdateProtocol.Json().DeserializeObject(Encoding.UTF8.GetString(data));
            if (Convert.ToBoolean(json["draft"]) || Convert.ToBoolean(json["prerelease"])) return null;
            var version = Convert.ToString(json["tag_name"])!;
            if (UpdateProtocol.ParseVersion(version) <= UpdateProtocol.ParseVersion(current)) return null;
            var page = Convert.ToString(json["html_url"])!;
            if (page != UpdateProtocol.ReleaseBase + "tag/" + Uri.EscapeDataString(version)) throw new InvalidDataException("更新页面不属于指定仓库或标签。");
            var release = new UpdateRelease { Version = version, Name = Convert.ToString(json["name"]) ?? version, Body = Convert.ToString(json["body"]) ?? "", Page = page };
            foreach (var value in (object[])json["assets"]) {
                var asset = (Dictionary<string, object>)value; var url = Convert.ToString(asset["browser_download_url"])!;
                if (!UpdateProtocol.AssetUrl(url)) throw new InvalidDataException("更新附件地址不属于指定仓库。");
                release.Assets.Add(new UpdateAsset { Name = Convert.ToString(asset["name"])!, Url = url, Bytes = Convert.ToInt64(asset["size"]), Digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) ?? "" : "" });
            }
            if (release.Assets.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1)) throw new InvalidDataException("更新附件重名。");
            return release;
        }
    }
    public sealed class UpdateSettings {
        public bool Enabled { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
        public long LastCheckUtcTicks { get; set; }
        public bool ShouldCheckAutomatically(DateTime nowUtc) {
            var elapsed = nowUtc.Ticks - LastCheckUtcTicks;
            return Enabled && (LastCheckUtcTicks == 0 || elapsed < 0 || elapsed >= TimeSpan.FromMinutes(30).Ticks);
        }
        public static string PathFor(string root) => Path.Combine(root, "PCL", "LocalUpdateSettings.json");
        public static UpdateSettings Read(string root) {
            try { var path = PathFor(root); return File.Exists(path) ? UpdateProtocol.Load<UpdateSettings>(path, 65536) ?? new UpdateSettings() : new UpdateSettings(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { return new UpdateSettings(); }
        }
        public void Write(string root) => UpdateProtocol.Save(PathFor(root), this);
    }
    public sealed class UpdateRequest {
        public int Format { get; set; } = 1;
        public string TransactionId { get; set; } = "";
        public string TargetDirectory { get; set; } = "";
        public string TargetExeName { get; set; } = "";
        public int ParentPid { get; set; }
        public long ParentStartUtcTicks { get; set; }
        public bool Restart { get; set; }
        public UpdateManifest Manifest { get; set; } = new UpdateManifest();
        public string TransactionDirectory => Path.Combine(TargetDirectory, "PCL", "LocalUpdates", TransactionId);
        public string TargetName(UpdateFile file) => file.Name == UpdateProtocol.LauncherName ? TargetExeName : file.Name;
        public void Validate(string requestPath) {
            if (Format != 1 || !Guid.TryParseExact(TransactionId, "N", out _) || ParentPid <= 0 || ParentStartUtcTicks <= 0 || !Path.IsPathRooted(TargetDirectory) || Manifest == null) throw new InvalidDataException("更新请求无效。");
            FileSafety.NoLinks(TargetDirectory); FileSafety.NoLinks(requestPath);
            if (!FileSafety.Full(Path.Combine(TransactionDirectory, "request.json")).Equals(FileSafety.Full(requestPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新请求必须保存在目标启动器自己的事务目录。");
            UpdateProtocol.Leaf(TargetExeName);
            if (!TargetExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || UpdateProtocol.PayloadNames.Skip(1).Contains(TargetExeName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("启动器文件名与组件冲突。");
            Manifest.Validate(Manifest.Version);
        }
    }
}
