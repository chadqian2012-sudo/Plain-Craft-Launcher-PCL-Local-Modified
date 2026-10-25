using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace PclLocalTools {
    public sealed class WorldInfo {
        public string Name { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Location { get; set; } = "";
        public string GameVersion { get; set; } = "未知";
        public DateTime? LastPlayed { get; set; }
        public long Bytes { get; set; } = -1;
        public string Note { get; set; } = "";
        public string LastPlayedText => LastPlayed?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";
        public string SizeText => Bytes < 0 ? "未计算" : (Bytes / 1048576d).ToString("N1") + " MiB";
    }
    public static class WorldCatalog {
        public static List<WorldInfo> Scan(string root, CancellationToken cancellation, IProgress<string>? progress = null) {
            FileSafety.NoLinks(root);
            var locations = new List<Tuple<string, string>> { Tuple.Create(Path.Combine(root, "saves"), "共享目录") };
            var versions = Path.Combine(root, "versions");
            if (Directory.Exists(versions)) {
                FileSafety.NoLinks(versions);
                foreach (var dir in new DirectoryInfo(versions).EnumerateDirectories()) {
                    cancellation.ThrowIfCancellationRequested();
                    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    locations.Add(Tuple.Create(Path.Combine(dir.FullName, "saves"), "版本目录：" + dir.Name));
                }
            }
            var result = new List<WorldInfo>();
            foreach (var pair in locations) {
                if (!Directory.Exists(pair.Item1)) continue;
                FileSafety.NoLinks(pair.Item1);
                foreach (var dir in new DirectoryInfo(pair.Item1).EnumerateDirectories()) {
                    cancellation.ThrowIfCancellationRequested();
                    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || !File.Exists(Path.Combine(dir.FullName, "level.dat"))) continue;
                    progress?.Report("读取世界：" + dir.Name);
                    var item = new WorldInfo { Name = dir.Name, Location = dir.FullName, Owner = pair.Item2 };
                    try {
                        var metadata = LevelMetadata.Read(Path.Combine(dir.FullName, "level.dat"));
                        item.Name = string.IsNullOrWhiteSpace(metadata.Name) ? dir.Name : metadata.Name;
                        item.LastPlayed = metadata.LastPlayed; item.GameVersion = metadata.Version;
                    } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException) { item.Note = "level.dat 暂不可读，稍后重试"; }
                    // Scan world names first; sizing large region files is an explicit separate operation.
                    result.Add(item);
                }
            }
            return result.OrderByDescending(w => w.LastPlayed ?? DateTime.MinValue).ToList();
        }
        public static long Size(string world, CancellationToken cancellation) => FileSafety.Files(world, cancellation).Sum(f => f.Length);
    }
    public sealed class LevelMetadata {
        public string Name = ""; public string Version = "未知"; public DateTime? LastPlayed;
        private long consumed;
        private byte[] Bytes(Stream stream, int count) {
            if (count < 0 || (consumed += count) > 16 * 1024 * 1024) throw new InvalidDataException("level.dat 超出读取上限。");
            var bytes = new byte[count]; int offset = 0, n;
            while (offset < count && (n = stream.Read(bytes, offset, count - offset)) > 0) offset += n;
            if (offset != count) throw new EndOfStreamException(); return bytes;
        }
        private int Byte(Stream s) => Bytes(s, 1)[0];
        private int Int(Stream s) { var b = Bytes(s, 4); return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; }
        private long Long(Stream s) { var b = Bytes(s, 8); long value = 0; foreach (byte part in b) value = (value << 8) | part; return value; }
        private string Text(Stream s) { var b = Bytes(s, 2); return Encoding.UTF8.GetString(Bytes(s, (b[0] << 8) | b[1])); }
        private void Skip(Stream s, long count) {
            if (count < 0 || count > 16 * 1024 * 1024) throw new InvalidDataException("NBT 数组长度无效。");
            while (count > 0) { var n = (int)Math.Min(count, 8192); Bytes(s, n); count -= n; }
        }
        private void Tag(Stream s, int type, string path, int depth) {
            if (depth > 64) throw new InvalidDataException("NBT 嵌套过深。");
            switch (type) {
                case 1: Skip(s, 1); break; case 2: Skip(s, 2); break; case 3: Skip(s, 4); break;
                case 4:
                    var value = Long(s);
                    if (path == "Data/LastPlayed" && value >= 0 && value <= 253402300799999L) LastPlayed = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(value);
                    break;
                case 5: Skip(s, 4); break; case 6: Skip(s, 8); break;
                case 7: Skip(s, Int(s)); break;
                case 8:
                    var text = Text(s); if (path == "Data/LevelName") Name = text; else if (path == "Data/Version/Name") Version = text;
                    break;
                case 9:
                    var childType = Byte(s); var count = Int(s);
                    if (count < 0 || count > 1000000 || (childType == 0 && count != 0)) throw new InvalidDataException("NBT 列表无效。");
                    for (int i = 0; i < count; i++) Tag(s, childType, path + "[]", depth + 1);
                    break;
                case 10:
                    int next;
                    while ((next = Byte(s)) != 0) { var name = Text(s); Tag(s, next, path.Length == 0 ? name : path + "/" + name, depth + 1); }
                    break;
                case 11: Skip(s, checked((long)Int(s) * 4)); break;
                case 12: Skip(s, checked((long)Int(s) * 8)); break;
                default: throw new InvalidDataException("未知 NBT 类型。");
            }
        }
        public static LevelMetadata Read(string file) {
            FileSafety.NoLinks(file);
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress)) {
                var metadata = new LevelMetadata();
                if (metadata.Byte(gzip) != 10) throw new InvalidDataException("level.dat 根节点不是 compound。");
                metadata.Text(gzip); metadata.Tag(gzip, 10, "", 0); return metadata;
            }
        }
    }
}
