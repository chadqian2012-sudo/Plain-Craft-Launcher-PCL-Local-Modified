using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace PclLocalTools {
    public static class FileSafety {
        public static string Full(string path) {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空。");
            var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full);
            return full.Equals(root, StringComparison.OrdinalIgnoreCase) ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        public static bool Within(string path, string parent) {
            var p = Full(path); var root = Full(parent);
            return p.Equals(root, StringComparison.OrdinalIgnoreCase) || p.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static void NoLinks(string path) {
            var current = new DirectoryInfo(Path.GetFullPath(path));
            while (current != null) {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("不处理符号链接、目录联接或云占位目录：" + current.FullName);
                current = current.Parent;
            }
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不处理链接文件：" + path);
        }
        public static string SafeRelative(string entry) {
            var p = entry.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(p) || p.StartsWith("/") || p.Contains(":")) throw new IOException("备份路径无效。\n" + p);
            foreach (var segment in p.Split('/')) {
                if (segment == ".." || segment == "." || segment.Length == 0 || segment.EndsWith(".") || segment.EndsWith(" ") || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new IOException("备份包含不安全的路径：" + p);
                var stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9]$"))
                    throw new IOException("备份包含 Windows 保留名称：" + p);
            }
            return p;
        }
        public static List<FileInfo> Files(string root, CancellationToken cancellation, Func<string, bool>? skip = null) {
            NoLinks(root);
            var output = new List<FileInfo>(); var pending = new Stack<DirectoryInfo>(); pending.Push(new DirectoryInfo(root));
            while (pending.Count > 0) {
                cancellation.ThrowIfCancellationRequested();
                var dir = pending.Pop(); NoLinks(dir.FullName);
                foreach (var item in dir.EnumerateFileSystemInfos()) {
                    cancellation.ThrowIfCancellationRequested();
                    if (skip != null && skip(item.Name)) continue;
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("发现链接，操作已停止：" + item.FullName);
                    if (item is DirectoryInfo child) pending.Push(child); else output.Add((FileInfo)item);
                    if (output.Count > 200000) throw new IOException("文件数量超过 200000，请缩小操作范围。");
                }
            }
            return output;
        }
        public static string Relative(string root, string path) {
            if (!Within(path, root) || Full(path).Equals(Full(root), StringComparison.OrdinalIgnoreCase)) throw new IOException("路径不在所选目录内。");
            return SafeRelative(Full(path).Substring(Full(root).Length + 1));
        }
        public static string Hash(Stream stream, CancellationToken cancellation) {
            using (var sha = SHA256.Create()) {
                var buffer = new byte[128 * 1024]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { cancellation.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, read, null, 0); }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return BitConverter.ToString(sha.Hash!).Replace("-", "");
            }
        }
        public static void DistinctTrees(string source, string destination) {
            NoLinks(source); NoLinks(destination);
            if (Within(source, destination) || Within(destination, source)) throw new IOException("源目录与目标目录必须互不包含。");
        }
        public static FileStream LockWorld(string world) {
            NoLinks(world);
            var path = Path.Combine(world, "session.lock"); NoLinks(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("这个世界可能正在运行或被占用。请退出该世界后再备份/迁移：" + world, ex); }
        }
    }
}
