using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace PclLocalTools {
    public sealed class UpdateBackup {
        public string Name { get; set; } = "";
        public bool Existed { get; set; }
        public string OldSha256 { get; set; } = "";
        public string NewSha256 { get; set; } = "";
    }
    public sealed class UpdateJournal {
        public string State { get; set; } = "Prepared";
        public List<UpdateBackup> Files { get; set; } = new List<UpdateBackup>();
    }
    public static class UpdateInstaller {
        private static string HashFile(string path) { using (var s = File.OpenRead(path)) return FileSafety.Hash(s, CancellationToken.None); }
        public static string Prepare(string archive, UpdateManifest manifest, string transaction, CancellationToken cancellation) {
            manifest.Validate(manifest.Version); FileSafety.NoLinks(archive); FileSafety.NoLinks(transaction);
            using (var input = File.OpenRead(archive)) if (!FileSafety.Hash(input, cancellation).Equals(manifest.ArchiveSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新 ZIP 的 SHA-256 不匹配。");
            var payload = Path.Combine(transaction, "payload");
            if (Directory.Exists(payload)) throw new IOException("更新暂存目录已存在，请重新下载。");
            using (var zip = ZipFile.OpenRead(archive)) {
                if (zip.Entries.Count > 500) throw new InvalidDataException("更新 ZIP 文件数量异常。");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries) {
                    var name = FileSafety.SafeRelative(entry.FullName.TrimEnd('/'));
                    if (!seen.Add(name)) throw new InvalidDataException("更新 ZIP 存在重复文件。");
                }
                foreach (var file in manifest.Files) {
                    var entry = zip.GetEntry(file.Name);
                    if (entry == null || entry.Length != file.Bytes) throw new InvalidDataException("更新 ZIP 缺少完整组件：" + file.Name);
                }
                Directory.CreateDirectory(payload);
                foreach (var file in manifest.Files) {
                    cancellation.ThrowIfCancellationRequested(); var output = Path.Combine(payload, file.Name); FileSafety.NoLinks(output);
                    using (var input = zip.GetEntry(file.Name)!.Open())
                    using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        var buffer = new byte[65536]; long total = 0; int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { cancellation.ThrowIfCancellationRequested(); total += n; if (total > file.Bytes) throw new InvalidDataException("更新文件展开大小异常。"); writer.Write(buffer, 0, n); }
                        if (total != file.Bytes) throw new InvalidDataException("更新组件展开不完整。");
                    }
                    if (!HashFile(output).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新组件校验失败：" + file.Name);
                }
            }
            return payload;
        }
        private static void WaitForParent(UpdateRequest request) {
            try {
                using (var parent = Process.GetProcessById(request.ParentPid)) {
                    if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks) return;
                    for (int i = 0; i < 240; i++) {
                        if (File.Exists(Path.Combine(request.TransactionDirectory, "cancel.flag"))) throw new IOException("更新安装已取消。");
                        if (parent.WaitForExit(500)) return;
                    }
                    throw new IOException("启动器没有退出，已取消安装；没有强行结束任何进程。");
                }
            } catch (ArgumentException) { /* Parent already exited. */ }
        }
        private static void Available(string target) {
            FileSafety.NoLinks(target);
            if (File.Exists(target)) using (File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        private static void Replace(string source, string target, string expected) {
            FileSafety.NoLinks(source); FileSafety.NoLinks(target);
            var incoming = target + ".incoming-" + Guid.NewGuid().ToString("N");
            File.Copy(source, incoming, false);
            if (!HashFile(incoming).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("待替换文件写入校验失败。");
            if (File.Exists(target)) File.Replace(incoming, target, null); else File.Move(incoming, target);
            if (!HashFile(target).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装后的程序校验失败。");
        }
        public static void Apply(string requestPath) => ApplyInternal(requestPath, true, null);
        internal static void ApplyInternal(string requestPath, bool waitForParent, Action<int>? beforeReplace) {
            var request = UpdateProtocol.Load<UpdateRequest>(requestPath); request.Validate(requestPath);
            if (waitForParent) WaitForParent(request);
            if (File.Exists(Path.Combine(request.TransactionDirectory, "cancel.flag"))) throw new IOException("更新安装已取消。");
            var transaction = request.TransactionDirectory; var payload = Path.Combine(transaction, "payload"); var backup = Path.Combine(transaction, "backup"); var journalPath = Path.Combine(transaction, "journal.json");
            FileSafety.NoLinks(payload); FileSafety.NoLinks(backup);
            if (File.Exists(journalPath) || Directory.Exists(backup)) throw new IOException("此更新事务已经使用过，请重新检查更新。");
            if (!File.Exists(Path.Combine(request.TargetDirectory, request.TargetExeName))) throw new IOException("原启动器不存在，拒绝安装。");
            foreach (var file in request.Manifest.Files) {
                var input = Path.Combine(payload, file.Name); FileSafety.NoLinks(input);
                if (new FileInfo(input).Length != file.Bytes || !HashFile(input).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("暂存组件损坏：" + file.Name);
                Available(Path.Combine(request.TargetDirectory, request.TargetName(file)));
            }
            Directory.CreateDirectory(backup); var journal = new UpdateJournal();
            foreach (var file in request.Manifest.Files) {
                var name = request.TargetName(file); var target = Path.Combine(request.TargetDirectory, name);
                var item = new UpdateBackup { Name = name, Existed = File.Exists(target), NewSha256 = file.Sha256 };
                if (item.Existed) {
                    item.OldSha256 = HashFile(target); var saved = Path.Combine(backup, name); File.Copy(target, saved, false);
                    if (HashFile(saved) != item.OldSha256) throw new IOException("旧程序备份校验失败。");
                }
                journal.Files.Add(item);
            }
            UpdateProtocol.Save(journalPath, journal); journal.State = "Applying"; UpdateProtocol.Save(journalPath, journal);
            try {
                for (int i = 0; i < request.Manifest.Files.Count; i++) {
                    beforeReplace?.Invoke(i); var file = request.Manifest.Files[i];
                    Replace(Path.Combine(payload, file.Name), Path.Combine(request.TargetDirectory, request.TargetName(file)), file.Sha256);
                }
                journal.State = "Completed"; UpdateProtocol.Save(journalPath, journal);
            } catch (Exception installError) {
                try { Restore(requestPath); }
                catch (Exception rollbackError) { throw new IOException("更新失败且回退尚未完成。请退出所有 PCL 后，运行更新助手恢复旧程序。事务目录：" + transaction, new AggregateException(installError, rollbackError)); }
                throw new IOException("更新未完成，旧程序已经恢复。", installError);
            }
        }
        public static void Restore(string requestPath) {
            var request = UpdateProtocol.Load<UpdateRequest>(requestPath); request.Validate(requestPath);
            var journalPath = Path.Combine(request.TransactionDirectory, "journal.json"); var journal = UpdateProtocol.Load<UpdateJournal>(journalPath);
            if (journal.State != "Applying" && journal.State != "RollbackPending") throw new IOException("没有需要回退的未完成事务。");
            var allowed = new HashSet<string>(request.Manifest.Files.Select(request.TargetName), StringComparer.OrdinalIgnoreCase);
            if (journal.Files.Count != allowed.Count || !new HashSet<string>(journal.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase).SetEquals(allowed)) throw new InvalidDataException("备份记录包含不允许的目标文件。");
            // Validate every backup and target before making any recovery changes.
            foreach (var file in journal.Files) {
                UpdateProtocol.Leaf(file.Name); var target = Path.Combine(request.TargetDirectory, file.Name); Available(target);
                if (!UpdateProtocol.HashText(file.NewSha256) || (file.Existed && !UpdateProtocol.HashText(file.OldSha256))) throw new InvalidDataException("回退校验记录无效。");
                if (file.Existed) {
                    var saved = Path.Combine(request.TransactionDirectory, "backup", file.Name); FileSafety.NoLinks(saved);
                    if (!HashFile(saved).Equals(file.OldSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份已损坏，拒绝回退。");
                }
                if (File.Exists(target)) { var hash = HashFile(target); if (!hash.Equals(file.NewSha256, StringComparison.OrdinalIgnoreCase) && !hash.Equals(file.OldSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("目标文件已被另行修改，停止回退：" + file.Name); }
            }
            journal.State = "RollbackPending"; UpdateProtocol.Save(journalPath, journal);
            foreach (var file in journal.Files) {
                var target = Path.Combine(request.TargetDirectory, file.Name);
                if (file.Existed) { if (!File.Exists(target) || !HashFile(target).Equals(file.OldSha256, StringComparison.OrdinalIgnoreCase)) Replace(Path.Combine(request.TransactionDirectory, "backup", file.Name), target, file.OldSha256); }
                else if (File.Exists(target)) File.Delete(target); // Only an allowlisted, verified new component.
            }
            journal.State = "RolledBack"; UpdateProtocol.Save(journalPath, journal);
        }
    }
}
