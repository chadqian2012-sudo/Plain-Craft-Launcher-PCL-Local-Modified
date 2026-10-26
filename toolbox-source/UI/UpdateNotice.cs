using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PclLocalTools {
    public sealed class UpdateNotice : Window {
        private readonly UpdateRelease release;
        private readonly string root, exe;
        private readonly Func<GitHubUpdateClient> createClient;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Button update;
        private readonly TextBlock status;
        private readonly CheckBox restart;
        private UpdateRequest? ready;
        private bool busy, helperStarted;
        public UpdateNotice(UpdateRelease release, string root, string exe) : this(release, root, exe, () => new GitHubUpdateClient()) { }
        internal UpdateNotice(UpdateRelease release, string root, string exe, Func<GitHubUpdateClient> createClient) {
            this.release = release; this.root = root; this.exe = exe; this.createClient = createClient;
            Title = "PCL 有新版本"; Width = 590; Height = 470; MinWidth = 480; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; Topmost = false;
            var panel = new DockPanel { Margin = new Thickness(22) }; Content = panel;
            var heading = new TextBlock { Text = "发现本仓库新版本 " + release.Version, FontSize = 22, Foreground = Brushes.SteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            var source = new TextBlock { Text = "当前版本：" + UpdateProtocol.Version + "\n更新源：" + UpdateProtocol.Repository + "\n可以关闭此窗口，更新不会自动下载或强制安装。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(source, Dock.Top); panel.Children.Add(source);
            var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
            status = new TextBlock { Text = release.SupportsInstall ? "点击下载后会校验更新包，安装前还需要你确认退出启动器。" : "此发行版未提供 PCL-update.json，请打开发布页手动下载。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) }; bottom.Children.Add(status);
            restart = new CheckBox { Content = "更新成功后重新打开启动器", IsChecked = false, Margin = new Thickness(0, 0, 0, 8) }; bottom.Children.Add(restart);
            var buttons = new WrapPanel(); bottom.Children.Add(buttons);
            update = Button(release.SupportsInstall ? "下载更新" : "打开发布页", async () => { if (!release.SupportsInstall) { Integration.OpenUrl(release.Page); return; } if (ready == null) await Download(); else Install(); }); buttons.Children.Add(update);
            buttons.Children.Add(Button("稍后", () => { Close(); return Task.CompletedTask; }));
            buttons.Children.Add(Button("跳过此版本", () => {
                var settings = UpdateSettings.Read(root); settings.SkippedVersion = release.Version; settings.Write(root); Close(); return Task.CompletedTask;
            }));
            buttons.Children.Add(Button("查看发布页", () => { Integration.OpenUrl(release.Page); return Task.CompletedTask; }));
            var text = release.Body.Length > 6000 ? release.Body.Substring(0, 6000) + "\n…完整说明请查看发布页。" : release.Body;
            panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) });
            Closing += (_, __) => { if (!helperStarted) cancellation.Cancel(); };
        }
        private Button Button(string text, Func<Task> action) {
            var button = new Button { Content = text, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 8, 5) };
            button.Click += async (_, __) => { try { await action(); } catch (OperationCanceledException) { status.Text = "更新下载已取消。"; } catch (Exception ex) { status.Text = "未安装更新：" + ex.Message; } }; return button;
        }
        private async Task Download() {
            if (busy) return; busy = true; update.IsEnabled = false;
            try {
                if (!File.Exists(Path.Combine(root, UpdateProtocol.HelperName))) throw new IOException("缺少更新助手，请从发布页完整下载程序包。");
                using (var client = createClient()) {
                    status.Text = "检查更新清单…"; var manifest = await client.Manifest(release, cancellation.Token);
                    var request = new UpdateRequest { TransactionId = Guid.NewGuid().ToString("N"), TargetDirectory = Path.GetFullPath(root), TargetExeName = exe, ParentPid = Process.GetCurrentProcess().Id, ParentStartUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Manifest = manifest };
                    var archive = await client.Download(release, manifest, request.TransactionDirectory, cancellation.Token, new Progress<string>(p => status.Text = p));
                    status.Text = "逐文件校验程序组件…";
                    await Task.Run(() => UpdateInstaller.Prepare(archive, manifest, request.TransactionDirectory, cancellation.Token));
                    cancellation.Token.ThrowIfCancellationRequested(); ready = request; update.Content = "退出启动器并安装";
                    status.Text = "更新已下载并通过 SHA-256 校验。你可以稍后再安装；安装前请先结束游戏和下载任务。";
                }
            } finally { busy = false; update.IsEnabled = true; }
        }
        private void Install() {
            if (ready == null || busy) return;
            if (MessageBox.Show(this, "是否正常退出启动器并安装已校验的更新？\n\n请先结束游戏及下载任务。更新助手只会在启动器退出后替换程序文件，并保留旧程序备份；不会强行结束游戏或其他进程。", "确认安装更新", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var main = Owner ?? Application.Current?.MainWindow;
            if (main == null || main == this) throw new InvalidOperationException("请从 PCL 主程序内执行更新。");
            ready.Restart = restart.IsChecked == true; var path = Path.Combine(ready.TransactionDirectory, "request.json"); ready.Validate(path); UpdateProtocol.Save(path, ready);
            var runner = Path.Combine(ready.TransactionDirectory, "runner"); Directory.CreateDirectory(runner); FileSafety.NoLinks(runner);
            foreach (var name in new[] { UpdateProtocol.HelperName, UpdateProtocol.HelperName + ".config" }) {
                var from = Path.Combine(root, name); var to = Path.Combine(runner, name); FileSafety.NoLinks(from); File.Copy(from, to, false);
                using (var a = File.OpenRead(from)) using (var b = File.OpenRead(to)) if (FileSafety.Hash(a, CancellationToken.None) != FileSafety.Hash(b, CancellationToken.None)) throw new IOException("更新助手副本校验失败。");
            }
            var process = Process.Start(new ProcessStartInfo(Path.Combine(runner, UpdateProtocol.HelperName), "--apply \"" + path + "\"") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (process == null) throw new IOException("更新助手启动失败。");
            helperStarted = true; update.IsEnabled = false; status.Text = "正在等待启动器正常退出；120 秒内没有退出则取消安装。";
            // PCL renders its task-exit confirmation inside the main window. An
            // owned updater window would cover it, so yield the screen first.
            Hide();
            try { main.Close(); }
            catch {
                File.WriteAllText(Path.Combine(ready.TransactionDirectory, "cancel.flag"), "Launcher close failed.");
                Show(); throw;
            }
            if (CloseWasCancelled(main.IsVisible, main.IsHitTestVisible)) {
                File.WriteAllText(Path.Combine(ready.TransactionDirectory, "cancel.flag"), "Launcher close was cancelled.");
                status.Text = "启动器未退出，本次安装已取消。结束游戏或下载后，可以重新检查更新。";
                Show();
            }
        }
        // PCL cancels the synchronous Closing event even on an accepted exit, then
        // disables input and exits after its animation. Visibility alone is not a cancellation signal.
        internal static bool CloseWasCancelled(bool visible, bool acceptsInput) => visible && acceptsInput;
    }
}
