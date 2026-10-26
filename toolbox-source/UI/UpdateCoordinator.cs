using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PclLocalTools {
    public static class UpdateCoordinator {
        private static int scheduled;
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private static string? root, exe;
        private static Window? owner;
        private static UpdateNotice? notice;
        public static string? Root => root;
        public static void RegisterHost(Assembly assembly) {
            if (assembly.GetType("PCL.FormMain") == null) return;
            exe = Path.GetFileName(assembly.Location); root = Path.GetDirectoryName(assembly.Location);
        }
        public static void Start(object main) {
            try {
                if (!(main is Window window) || Interlocked.Exchange(ref scheduled, 1) != 0) return;
                RegisterHost(main.GetType().Assembly); owner = window;
                if (root == null) return;
                var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromSeconds(8) };
                timer.Tick += (_, __) => { timer.Stop(); _ = Check(false); }; timer.Start();
            } catch (Exception ex) { Trace.WriteLine("Local update startup check unavailable: " + ex.Message); }
        }
        public static void CheckNow() { _ = Check(true); }
        private static async Task Check(bool manual) {
            if (root == null || exe == null) { if (manual) MessageBox.Show("请在 PCL 启动器内检查并安装更新。独立工具箱可以打开仓库发布页下载。", "PCL 检查更新"); return; }
            if (notice?.IsVisible == true) { if (manual) notice.Activate(); return; }
            if (!gate.Wait(0)) { if (manual) MessageBox.Show("正在检查更新，请稍候。", "PCL 检查更新"); return; }
            try {
                var settings = UpdateSettings.Read(root);
                if (!manual && !settings.ShouldCheckAutomatically(DateTime.UtcNow)) return;
                UpdateRelease? release;
                using (var client = new GitHubUpdateClient()) release = await client.Check(UpdateProtocol.Version, CancellationToken.None);
                settings = UpdateSettings.Read(root); // Preserve settings changed while the request was in flight.
                settings.LastCheckUtcTicks = DateTime.UtcNow.Ticks;
                try { settings.Write(root); } catch (Exception ex) { Trace.WriteLine("Could not cache update settings: " + ex.Message); }
                if (release == null) { if (manual) MessageBox.Show("当前版本 " + UpdateProtocol.Version + "，未发现本仓库更高的正式版本。", "PCL 检查更新"); return; }
                if (!manual && !settings.Enabled) return;
                if (!manual && settings.SkippedVersion.Length != 0) {
                    try { if (UpdateProtocol.ParseVersion(settings.SkippedVersion) == UpdateProtocol.ParseVersion(release.Version)) return; }
                    catch (InvalidDataException) { /* Ignore a manually corrupted skip value. */ }
                }
                Action show = () => {
                    if (notice?.IsVisible == true || Application.Current?.Dispatcher.HasShutdownStarted != false) return;
                    notice = new UpdateNotice(release, root, exe);
                    if (owner?.IsVisible == true) notice.Owner = owner;
                    notice.ShowActivated = manual; notice.Show();
                };
                // Background checks do not interrupt a game or another application in the foreground.
                if (!manual && owner != null && !owner.IsActive) {
                    EventHandler? activated = null;
                    activated = (_, __) => { owner.Activated -= activated; show(); };
                    owner.Activated += activated;
                } else show();
            } catch (Exception ex) {
                if (manual) MessageBox.Show("暂时无法检查更新，启动器可以继续使用。\n" + ex.Message, "PCL 检查更新");
                else Trace.WriteLine("Local update check failed: " + ex.Message);
            } finally { gate.Release(); }
        }
        public static void AttachSystemSettings(object page) {
            try {
                var property = page.GetType().GetProperty("ComboSystemUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(page) is FrameworkElement combo) combo.ToolTip = "此项属于已禁用的上游更新。你仓库的更新检查开关位于：更多 → 关于 → 本地工具箱 → 版本与更新。";
            } catch (Exception ex) { Trace.WriteLine(ex.Message); }
        }
    }
}
