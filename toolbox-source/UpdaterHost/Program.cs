using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using PclLocalTools;
internal static class Program {
    [STAThread]
    private static int Main(string[] args) {
        var quiet = args.Length > 0 && args[args.Length - 1] == "--quiet";
        if (quiet) args = args.Take(args.Length - 1).ToArray();
        try {
            if (args.Length == 2 && args[0] == "--apply") {
                var request = UpdateProtocol.Load<UpdateRequest>(args[1]);
                UpdateInstaller.Apply(args[1]);
                if (request.Restart) Process.Start(new ProcessStartInfo(Path.Combine(request.TargetDirectory, request.TargetExeName)) { WorkingDirectory = request.TargetDirectory, UseShellExecute = true });
                return 0;
            }
            if (args.Length == 4 && args[0] == "--recover") {
                var request = UpdateProtocol.Load<UpdateRequest>(args[1]); request.Validate(args[1]);
                if (!FileSafety.Within(AppDomain.CurrentDomain.BaseDirectory, request.TransactionDirectory)) throw new IOException("恢复副本不在所属事务目录内。");
                WaitForRecoveryParent(int.Parse(args[2], CultureInfo.InvariantCulture), long.Parse(args[3], CultureInfo.InvariantCulture));
                UpdateInstaller.Restore(args[1]); if (!quiet) MessageBox.Show("旧程序已恢复。", "PCL 更新恢复"); return 0;
            }
            if (args.Length == 2 && args[0] == "--rollback") { Recover(args[1], quiet); return 0; }
            if (args.Length != 0) throw new ArgumentException("更新助手参数无效。");
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCL", "LocalUpdates");
            var declined = false;
            if (Directory.Exists(root)) {
                FileSafety.NoLinks(root);
                foreach (var dir in new DirectoryInfo(root).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc)) {
                    var path = Path.Combine(dir.FullName, "journal.json"); if (!File.Exists(path)) continue;
                    var journal = UpdateProtocol.Load<UpdateJournal>(path);
                    if (journal.State != "Applying" && journal.State != "RollbackPending") continue;
                    if (MessageBox.Show("发现未完成的更新。请先退出所有 PCL 与工具箱。是否恢复该事务备份的旧程序？\n" + dir.Name, "PCL 更新恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                        Recover(Path.Combine(dir.FullName, "request.json"), quiet); return 0;
                    }
                    declined = true;
                }
            }
            MessageBox.Show(declined ? "已跳过本次恢复，程序文件保持原样。需要时可重新运行更新助手。" : "未发现需要恢复的更新事务。正常更新请在 PCL 中检查新版。", "PCL 更新助手"); return 0;
        } catch (Exception ex) { if (!quiet) MessageBox.Show(ex.Message, "PCL 更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
    }
    private static void Recover(string requestPath, bool quiet) {
        var request = UpdateProtocol.Load<UpdateRequest>(requestPath); request.Validate(requestPath);
        if (!FileSafety.Full(AppDomain.CurrentDomain.BaseDirectory).Equals(FileSafety.Full(request.TargetDirectory), StringComparison.OrdinalIgnoreCase)) {
            UpdateInstaller.Restore(requestPath); if (!quiet) MessageBox.Show("旧程序已恢复。", "PCL 更新恢复"); return;
        }
        // A helper in the installation directory locks its own EXE. Transfer to a
        // verified transaction-local copy, exit, then let that copy restore files.
        var runner = Path.Combine(request.TransactionDirectory, "recovery-" + Guid.NewGuid().ToString("N"));
        FileSafety.NoLinks(runner); Directory.CreateDirectory(runner);
        foreach (var name in new[] { UpdateProtocol.HelperName, UpdateProtocol.HelperName + ".config" }) {
            var from = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name); var to = Path.Combine(runner, name); FileSafety.NoLinks(from);
            File.Copy(from, to, false);
            using (var a = File.OpenRead(from)) using (var b = File.OpenRead(to)) if (FileSafety.Hash(a, CancellationToken.None) != FileSafety.Hash(b, CancellationToken.None)) throw new IOException("恢复助手副本校验失败。");
        }
        using (var parent = Process.GetCurrentProcess()) {
            var arguments = "--recover \"" + requestPath + "\" " + parent.Id.ToString(CultureInfo.InvariantCulture) + " " + parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + (quiet ? " --quiet" : "");
            if (Process.Start(new ProcessStartInfo(Path.Combine(runner, UpdateProtocol.HelperName), arguments) { WorkingDirectory = request.TargetDirectory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }) == null) throw new IOException("恢复助手副本启动失败。");
        }
    }
    private static void WaitForRecoveryParent(int pid, long startTicks) {
        if (pid <= 0 || startTicks <= 0) throw new ArgumentException("恢复父进程信息无效。");
        try {
            using (var parent = Process.GetProcessById(pid)) {
                if (parent.StartTime.ToUniversalTime().Ticks != startTicks) return;
                if (!parent.WaitForExit(15000)) throw new IOException("原恢复助手尚未退出，已停止恢复；没有强制结束进程。");
            }
        } catch (ArgumentException) { }
    }
}
