using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PclLocalTools;
internal static class Program {
    [STAThread]
    private static int Main(string[] args) {
        var quiet = args.Length == 3 && args[2] == "--quiet";
        if (quiet) args = args.Take(2).ToArray();
        try {
            if (args.Length == 2 && args[0] == "--apply") {
                var request = UpdateProtocol.Load<UpdateRequest>(args[1]);
                UpdateInstaller.Apply(args[1]);
                if (request.Restart) Process.Start(new ProcessStartInfo(Path.Combine(request.TargetDirectory, request.TargetExeName)) { WorkingDirectory = request.TargetDirectory, UseShellExecute = true });
                return 0;
            }
            if (args.Length == 2 && args[0] == "--rollback") { UpdateInstaller.Restore(args[1]); if (!quiet) MessageBox.Show("旧程序已恢复。", "PCL 更新恢复"); return 0; }
            if (args.Length != 0) throw new ArgumentException("更新助手参数无效。");
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCL", "LocalUpdates");
            if (Directory.Exists(root)) {
                FileSafety.NoLinks(root);
                foreach (var dir in new DirectoryInfo(root).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc)) {
                    var path = Path.Combine(dir.FullName, "journal.json"); if (!File.Exists(path)) continue;
                    var journal = UpdateProtocol.Load<UpdateJournal>(path);
                    if (journal.State != "Applying" && journal.State != "RollbackPending") continue;
                    if (MessageBox.Show("发现未完成的更新。请先退出所有 PCL 与工具箱。是否恢复该事务备份的旧程序？\n" + dir.Name, "PCL 更新恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                        UpdateInstaller.Restore(Path.Combine(dir.FullName, "request.json")); MessageBox.Show("旧程序已恢复。", "PCL 更新恢复"); return 0;
                    }
                }
            }
            MessageBox.Show("未发现需要恢复的更新事务。正常更新请在 PCL 中检查新版。", "PCL 更新助手"); return 0;
        } catch (Exception ex) { if (!quiet) MessageBox.Show(ex.Message, "PCL 更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
    }
}
