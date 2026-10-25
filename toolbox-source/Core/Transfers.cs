using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PclLocalTools {
    public sealed class BackupFile {
        public string Path { get; set; } = "";
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = "";
        public DateTime ModifiedUtc { get; set; }
    }
    public sealed class BackupManifest {
        public int Format { get; set; } = 1;
        public string WorldName { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public List<BackupFile> Files { get; set; } = new List<BackupFile>();
    }
    public static class Transfers {
        public const string ManifestName = ".pcl-backup-manifest.json";
        private static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        private static bool Skip(string name) => name.Equals("session.lock", StringComparison.OrdinalIgnoreCase);
        private static string CopyHash(Stream input, Stream output, CancellationToken cancellation, long expected = -1) {
            using (var sha = SHA256.Create()) {
                var buffer = new byte[128 * 1024]; int read; long total = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                    cancellation.ThrowIfCancellationRequested(); total = checked(total + read);
                    if ((expected >= 0 && total > expected) || total > 128L * 1024 * 1024 * 1024) throw new InvalidDataException("文件展开大小超出清单声明。");
                    output.Write(buffer, 0, read); sha.TransformBlock(buffer, 0, read, null, 0);
                }
                if (expected >= 0 && total != expected) throw new InvalidDataException("文件大小与清单声明不一致。");
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return BitConverter.ToString(sha.Hash!).Replace("-", "");
            }
        }
        public static string Backup(string world, string zipPath, CancellationToken cancellation, IProgress<string>? progress = null) {
            FileSafety.NoLinks(world); FileSafety.NoLinks(zipPath);
            if (!File.Exists(Path.Combine(world, "level.dat"))) throw new IOException("所选目录不是包含 level.dat 的 Java 版世界。");
            if (FileSafety.Within(zipPath, world)) throw new IOException("备份 ZIP 必须保存到世界目录外。");
            if (File.Exists(zipPath)) throw new IOException("备份文件已存在，请换一个名称。");
            var temporary = zipPath + ".partial-" + Guid.NewGuid().ToString("N");
            using (FileSafety.LockWorld(world)) {
                var files = FileSafety.Files(world, cancellation, Skip);
                var originals = files.ToDictionary(f => f.FullName, f => Tuple.Create(f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
                if (files.Any(f => FileSafety.Relative(world, f.FullName).Equals(ManifestName, StringComparison.OrdinalIgnoreCase))) throw new IOException("世界内有与备份清单同名的文件。");
                var manifest = new BackupManifest { WorldName = new DirectoryInfo(world).Name, CreatedUtc = DateTime.UtcNow };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) {
                    int n = 0;
                    foreach (var file in files) {
                        cancellation.ThrowIfCancellationRequested(); FileSafety.NoLinks(file.FullName);
                        var relative = FileSafety.Relative(world, file.FullName);
                        progress?.Report($"备份 {++n}/{files.Count}：{relative}");
                        var beforeLength = file.Length; var beforeTime = file.LastWriteTimeUtc;
                        var entry = zip.CreateEntry(relative, CompressionLevel.Fastest);
                        string hash;
                        using (var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = entry.Open()) hash = CopyHash(input, output, cancellation, beforeLength);
                        file.Refresh();
                        if (!file.Exists || file.Length != beforeLength || file.LastWriteTimeUtc != beforeTime) throw new IOException("备份时源文件发生变化，未生成正式备份：" + relative);
                        manifest.Files.Add(new BackupFile { Path = relative, Bytes = beforeLength, ModifiedUtc = beforeTime, Sha256 = hash });
                    }
                    var currentFiles = FileSafety.Files(world, cancellation, Skip);
                    if (!new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase).SetEquals(currentFiles.Select(f => f.FullName))) throw new IOException("备份时世界文件清单发生变化。");
                    foreach (var file in currentFiles) if (file.Length != originals[file.FullName].Item1 || file.LastWriteTimeUtc != originals[file.FullName].Item2) throw new IOException("备份时源文件发生变化。");
                    using (var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open(), new UTF8Encoding(false))) writer.Write(Json().Serialize(manifest));
                }
                progress?.Report("逐文件核对备份 SHA-256…");
                VerifyBackup(temporary, cancellation);
                cancellation.ThrowIfCancellationRequested(); File.Move(temporary, zipPath);
                return zipPath;
            }
        }
        private static BackupManifest ReadManifest(ZipArchive zip) {
            var entries = zip.Entries.Where(e => e.FullName == ManifestName).ToArray();
            if (entries.Length != 1 || entries[0].Length > 32 * 1024 * 1024) throw new InvalidDataException("没有有效的 PCL 本地工具箱备份清单。");
            string content;
            using (var reader = new StreamReader(entries[0].Open(), Encoding.UTF8)) {
                var text = new StringBuilder(); var buffer = new char[4096]; int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) { if (text.Length + read > 32 * 1024 * 1024) throw new InvalidDataException("备份清单过大。"); text.Append(buffer, 0, read); }
                content = text.ToString();
            }
            var manifest = Json().Deserialize<BackupManifest>(content);
            if (manifest == null || manifest.Format != 1 || manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > 200000) throw new InvalidDataException("备份清单格式不支持。");
            long total = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files) {
                FileSafety.SafeRelative(file.Path);
                if (!seen.Add(file.Path) || file.Path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || file.Bytes < 0 || !System.Text.RegularExpressions.Regex.IsMatch(file.Sha256 ?? "", "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("备份清单存在重复或无效项目。");
                total = checked(total + file.Bytes);
                if (total > 128L * 1024 * 1024 * 1024) throw new InvalidDataException("备份展开大小超过 128 GiB。");
            }
            if (!seen.Contains("level.dat")) throw new InvalidDataException("备份缺少 level.dat。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries) {
                FileSafety.SafeRelative(entry.FullName);
                if (!names.Add(entry.FullName)) throw new InvalidDataException("ZIP 包含重复文件。");
            }
            if (zip.Entries.Count != manifest.Files.Count + 1 || manifest.Files.Any(f => zip.GetEntry(f.Path) == null || zip.GetEntry(f.Path)!.Length != f.Bytes)) throw new InvalidDataException("ZIP 文件与清单不一致。");
            return manifest;
        }
        public static BackupManifest VerifyBackup(string path, CancellationToken cancellation) {
            FileSafety.NoLinks(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                var manifest = ReadManifest(zip);
                foreach (var file in manifest.Files) {
                    cancellation.ThrowIfCancellationRequested();
                    using (var entry = zip.GetEntry(file.Path)!.Open()) if (!CopyHash(entry, Stream.Null, cancellation, file.Bytes).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份校验失败：" + file.Path);
                }
                return manifest;
            }
        }
        public static string RestoreAsNewWorld(string zipPath, string destination, CancellationToken cancellation, IProgress<string>? progress = null) {
            FileSafety.NoLinks(zipPath); FileSafety.NoLinks(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("恢复只允许创建新世界目录，目标名称已存在。");
            var staging = FileSafety.Full(destination) + ".partial-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                var manifest = ReadManifest(zip);
                Directory.CreateDirectory(staging); int n = 0;
                foreach (var file in manifest.Files) {
                    cancellation.ThrowIfCancellationRequested();
                    var output = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!FileSafety.Within(output, staging)) throw new InvalidDataException("备份路径越界。");
                    FileSafety.NoLinks(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    progress?.Report($"恢复 {++n}/{manifest.Files.Count}：{file.Path}");
                    string hash;
                    using (var input = zip.GetEntry(file.Path)!.Open())
                    using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) hash = CopyHash(input, writer, cancellation, file.Bytes);
                    if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("恢复校验失败：" + file.Path);
                    // Confirm the bytes on disk, rather than only the bytes sent to the file stream.
                    using (var verify = File.OpenRead(output)) if (FileSafety.Hash(verify, cancellation) != hash) throw new IOException("写入后校验失败：" + file.Path);
                    File.SetLastWriteTimeUtc(output, file.ModifiedUtc);
                }
            }
            cancellation.ThrowIfCancellationRequested(); FileSafety.NoLinks(destination); Directory.Move(staging, destination); return destination;
        }
        private static readonly HashSet<string> GameDirectories = new HashSet<string>(new[] { "versions", "libraries", "assets", "mods", "config", "saves", "resourcepacks", "shaderpacks", "screenshots", "defaultconfigs", "datapacks", "kubejs", "scripts", "schematics" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> GameFiles = new HashSet<string>(new[] { "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat", "servers.dat_old" }, StringComparer.OrdinalIgnoreCase);
        private static bool SkipMigration(string name) => Skip(name) || new[] { "logs", "crash-reports", ".git", "launcher_accounts.json", "accounts.json", "accounts.dat", "usercache.json", "launcher_profiles.json", "launcher_profiles_microsoft_store.json", "launcher_log.txt", ".pcl-migration-manifest.json" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        public static List<FileInfo> MigrationPlan(string source, CancellationToken cancellation) {
            FileSafety.NoLinks(source); var files = new List<FileInfo>();
            foreach (var dir in new DirectoryInfo(source).EnumerateDirectories()) if (GameDirectories.Contains(dir.Name)) files.AddRange(FileSafety.Files(dir.FullName, cancellation, SkipMigration));
            foreach (var file in new DirectoryInfo(source).EnumerateFiles()) if (GameFiles.Contains(file.Name)) { FileSafety.NoLinks(file.FullName); files.Add(file); }
            if (files.Count == 0) throw new IOException("没有找到可迁移的游戏文件。请选择包含 versions、saves 或 mods 的游戏根目录。");
            return files;
        }
        public static string Migrate(string source, string destination, CancellationToken cancellation, IProgress<string>? progress = null) {
            FileSafety.DistinctTrees(source, destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("目标必须是尚不存在的新目录，避免覆盖任何数据。");
            var worlds = WorldCatalog.Scan(source, cancellation);
            var locks = new List<FileStream>();
            var staging = FileSafety.Full(destination) + ".partial-" + Guid.NewGuid().ToString("N");
            try {
                foreach (var world in worlds) locks.Add(FileSafety.LockWorld(world.Location));
                var files = MigrationPlan(source, cancellation);
                var originals = files.ToDictionary(f => f.FullName, f => Tuple.Create(f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(staging); var manifest = new BackupManifest { WorldName = "Minecraft directory migration", CreatedUtc = DateTime.UtcNow }; int n = 0;
                foreach (var file in files) {
                    cancellation.ThrowIfCancellationRequested(); FileSafety.NoLinks(file.FullName);
                    var relative = FileSafety.Relative(source, file.FullName); var output = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                    FileSafety.NoLinks(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!); progress?.Report($"复制并校验 {++n}/{files.Count}：{relative}");
                    string hash;
                    using (var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) hash = CopyHash(input, writer, cancellation, originals[file.FullName].Item1);
                    using (var verify = File.OpenRead(output)) if (FileSafety.Hash(verify, cancellation) != hash) throw new IOException("目标文件校验失败：" + relative);
                    file.Refresh(); var original = originals[file.FullName];
                    if (!file.Exists || file.Length != original.Item1 || file.LastWriteTimeUtc != original.Item2) throw new IOException("复制期间源文件发生变化：" + relative);
                    File.SetLastWriteTimeUtc(output, original.Item2);
                    manifest.Files.Add(new BackupFile { Path = relative, Bytes = original.Item1, ModifiedUtc = original.Item2, Sha256 = hash });
                }
                var after = MigrationPlan(source, cancellation);
                if (!new HashSet<string>(originals.Keys, StringComparer.OrdinalIgnoreCase).SetEquals(after.Select(f => f.FullName))) throw new IOException("复制期间源目录文件清单发生变化。");
                foreach (var file in after) if (file.Length != originals[file.FullName].Item1 || file.LastWriteTimeUtc != originals[file.FullName].Item2) throw new IOException("复制期间源文件发生变化。");
                File.WriteAllText(Path.Combine(staging, ".pcl-migration-manifest.json"), Json().Serialize(manifest), new UTF8Encoding(false));
                cancellation.ThrowIfCancellationRequested(); FileSafety.NoLinks(destination); Directory.Move(staging, destination); return destination;
            } finally { foreach (var handle in locks) handle.Dispose(); }
        }
    }
}
