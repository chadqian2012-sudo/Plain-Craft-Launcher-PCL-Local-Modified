using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PclLocalTools {
    public sealed class ToolboxWindow : Window {
        private readonly LauncherContext context;
        private readonly TextBox root = Input();
        private readonly TextBlock status = Label("请选择操作。打开工具箱不会自动扫描、联网或修改游戏文件。");
        private readonly List<Button> actions = new List<Button>();
        private readonly ObservableCollection<WorldInfo> worlds = new ObservableCollection<WorldInfo>();
        private readonly ObservableCollection<NetworkResult> network = new ObservableCollection<NetworkResult>();
        private readonly DataGrid worldGrid;
        private readonly TextBox source = Input(), destination = Input(), logPath = Input(), logReport = Input(true), migrationPreview = Input(true);
        private readonly Button cancel;
        private CancellationTokenSource? running;
        private string? previewSource, previewDestination, copiedDestination;
        private string? report;
        private readonly TabControl tabs = new TabControl();
        public ToolboxWindow(LauncherContext context, int selectedTab = 0) {
            this.context = context; Title = "PCL 本地工具箱 2.99.2 开发预览"; Width = 1080; Height = 760; MinWidth = 800; MinHeight = 580;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; Background = new SolidColorBrush(Color.FromRgb(247, 249, 252)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
            root.Text = context.Root; source.Text = context.Root;
            var layout = new DockPanel { Margin = new Thickness(20) }; Content = layout;
            var title = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(title, Dock.Top); layout.Children.Add(title);
            title.Children.Add(new TextBlock { Text = "本地工具箱", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(28, 91, 155)) });
            title.Children.Add(Label("手动备份、复制与诊断 · 原作者：龙腾猫跃 · 本地修改：chadqian2012-sudo"));
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
            cancel = new Button { Content = "取消当前操作", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += (_, __) => { running?.Cancel(); status.Text = "正在取消，请等待当前文件操作结束。"; }; DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
            status.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(status);
            layout.Children.Add(tabs);

            var worldPage = Page("存档与路径");
            worldPage.Children.Add(Label("游戏根目录（包含 versions、saves、mods 的 .minecraft 目录）："));
            var rootRow = new DockPanel(); worldPage.Children.Add(rootRow);
            var chooseRoot = Action("选择目录", () => { var folder = ChooseFolder(); if (folder != null) { root.Text = folder; source.Text = folder; } }); DockPanel.SetDock(chooseRoot, Dock.Right); rootRow.Children.Add(chooseRoot); rootRow.Children.Add(root);
            if (context.KnownRoots.Count > 0) {
                var known = new ComboBox { ItemsSource = context.KnownRoots, Margin = new Thickness(0, 6, 0, 6), MinHeight = 28 };
                known.SelectionChanged += (_, __) => { if (known.SelectedItem is string path && running == null) { root.Text = path; source.Text = path; } }; worldPage.Children.Add(known);
            }
            worldPage.Children.Add(Label(context.EffectiveDirectory.Length > 0 ? "当前所选版本实际游戏目录：" + context.EffectiveDirectory + "\n实际存档目录：" + Path.Combine(context.EffectiveDirectory, "saves") : "世界列表会检查共享 saves 和各版本目录下的 saves；目录来源不等同于当前启用的隔离设置。"));
            var worldActions = new WrapPanel(); worldPage.Children.Add(worldActions);
            worldActions.Children.Add(AsyncAction("扫描世界", Scan));
            worldActions.Children.Add(Action("打开根目录", () => Integration.OpenFolder(root.Text)));
            worldActions.Children.Add(Action("打开所选世界", () => Integration.OpenFolder(SelectedWorld().Location)));
            worldActions.Children.Add(AsyncAction("计算所选大小", ComputeSize));
            worldActions.Children.Add(AsyncAction("备份所选世界", Backup));
            worldActions.Children.Add(AsyncAction("校验备份 ZIP", VerifyBackup));
            worldActions.Children.Add(AsyncAction("恢复为新世界", Restore));
            worldPage.Children.Add(Label("备份和迁移前请退出世界。操作期间会持有 session.lock；被游戏占用的世界会拒绝操作。恢复始终创建新目录。"));
            worldGrid = Grid(new[] { ("世界", "Name", 150d), ("游戏版本", "GameVersion", 100d), ("最后游玩", "LastPlayedText", 145d), ("大小", "SizeText", 85d), ("目录来源", "Owner", 150d), ("实际路径", "Location", 350d), ("备注", "Note", 180d) });
            worldGrid.ItemsSource = worlds; worldGrid.Height = 310; worldPage.Children.Add(worldGrid);
            root.TextChanged += (_, __) => { worlds.Clear(); };

            var migration = Page("目录复制迁移");
            migration.Children.Add(Label("将游戏、依赖、Mods、配置和存档复制到一个新目录；按 SHA-256 验证目标文件。源文件保持原位。"));
            migration.Children.Add(Label("源游戏根目录：")); migration.Children.Add(source);
            var chooseSource = Action("选择源目录", () => { var folder = ChooseFolder(); if (folder != null) source.Text = folder; }); migration.Children.Add(chooseSource);
            migration.Children.Add(Label("目标目录（必须尚不存在，并且与源目录互不包含）：")); migration.Children.Add(destination);
            migration.Children.Add(Action("选择目标的父目录", () => { var parent = ChooseFolder(); if (parent != null) destination.Text = Path.Combine(parent, ".minecraft-copy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); }));
            migration.Children.Add(Label("范围：versions / libraries / assets / mods / config / saves / resourcepacks / shaderpacks / screenshots / defaultconfigs / datapacks / kubejs / scripts / schematics，以及游戏选项和服务器列表。排除启动器账号文件、日志、崩溃报告、临时锁和仓库目录。"));
            var migrateActions = new WrapPanel(); migration.Children.Add(migrateActions);
            migrateActions.Children.Add(AsyncAction("预览文件与大小", PreviewMigration));
            migrateActions.Children.Add(AsyncAction("开始复制并校验", Migrate));
            migrateActions.Children.Add(Action("加入 PCL 文件夹列表", () => {
                if (copiedDestination == null) throw new InvalidOperationException("请先成功完成复制。");
                Integration.RegisterFolder(context, copiedDestination); status.Text = "已加入当前 PCL 的文件夹列表；重新打开版本选择页查看。";
            }));
            migrationPreview.Height = 230; migration.Children.Add(migrationPreview);
            source.TextChanged += (_, __) => InvalidatePreview(); destination.TextChanged += (_, __) => InvalidatePreview();

            var net = Page("下载诊断");
            net.Children.Add(Label("只有点击开始才联网。依次检查官方版本清单、版本元数据、客户端地址、资源索引、抽样资源/依赖库、Fabric 与 BMCLAPI。客户端只探测 1 字节；不会完整下载游戏，也不会修改下载设置。"));
            net.Children.Add(Label("该检查只能证明本次访问与抽样校验结果，不能保证所有下载源或所有文件都正常。"));
            var netActions = new WrapPanel(); net.Children.Add(netActions);
            netActions.Children.Add(AsyncAction("开始检查 / 重新检查", CheckNetwork));
            netActions.Children.Add(Action("导出诊断结果", () => {
                if (network.Count == 0) throw new InvalidOperationException("请先运行诊断。");
                SaveText("PCL-download-diagnostics.txt", string.Join("\r\n\r\n", network.Select(n => n.Name + "\r\n" + n.Url + "\r\n" + n.Status + " · " + n.Milliseconds + " ms")));
            }));
            var netGrid = Grid(new[] { ("检查项", "Name", 170d), ("结果", "Status", 390d), ("耗时 ms", "Milliseconds", 90d), ("实际地址", "Url", 400d) }); netGrid.Height = 350; netGrid.ItemsSource = network; net.Children.Add(netGrid);

            var logs = Page("启动与下载日志");
            logs.Children.Add(Label("按错误关键字给出排查建议，不自动修改 Java、内存或模组。最多读取日志末尾 2 MiB。导出内容会尝试隐藏令牌、邮箱、用户主目录和所选游戏根目录，分享前仍请复核。"));
            logs.Children.Add(logPath); var logActions = new WrapPanel(); logs.Children.Add(logActions);
            logActions.Children.Add(Action("选择日志", () => { var dialog = new OpenFileDialog { Filter = "日志与文本|*.log;*.txt|所有文件|*.*" }; if (dialog.ShowDialog(this) == true) logPath.Text = dialog.FileName; }));
            logActions.Children.Add(AsyncAction("分析日志", Analyze));
            logActions.Children.Add(Action("选取当前版本 latest.log", () => {
                var path = context.EffectiveDirectory.Length > 0 ? context.EffectiveDirectory : root.Text;
                logPath.Text = Path.Combine(path, "logs", "latest.log");
            }));
            logActions.Children.Add(Action("导出脱敏报告", () => { if (report == null) throw new InvalidOperationException("请先分析日志。"); SaveText("PCL-diagnostics.txt", report); }));
            logReport.Height = 350; logs.Children.Add(logReport); logPath.TextChanged += (_, __) => { report = null; logReport.Clear(); };

            var about = Page("版本与手动更新");
            about.Children.Add(new TextBlock { Text = "第三方本地修改版 2.99.2 开发预览", FontSize = 20, Margin = new Thickness(0, 8, 0, 10) });
            about.Children.Add(Label("基础版本：PCL 2.99.0.0。原作者：龙腾猫跃（LTCatt）。本地修改：chadqian2012-sudo。\n启动器自我更新、更新下载、文件替换和自动重启的实现继续保持移除。手动打开发布页后，由你选择下载和安装。"));
            about.Children.Add(Action("打开本修改版 Releases", Integration.OpenRelease));
            about.Children.Add(Action("打开本修改版源码仓库", () => Integration.OpenUrl(Integration.Repository)));
            about.Children.Add(Action("打开 PCL 上游仓库", () => Integration.OpenUrl("https://github.com/Meloong-Git/PCL")));
            about.Children.Add(Label("本地工具箱通过配套 PclLocalTools.dll 提供功能，请与启动器一起保留。备份、迁移或诊断仅在你点击时运行。\n复制中断会保留带 .partial- 标记的临时输出，不会将其显示为已完成备份/迁移。"));
            tabs.SelectedIndex = Math.Max(0, Math.Min(4, selectedTab));
            Closing += (_, e) => { if (running != null) { e.Cancel = true; running.Cancel(); status.Text = "正在取消操作，请等待结束后关闭窗口。"; } };
        }
        private static TextBox Input(bool readOnly = false) => new TextBox { IsReadOnly = readOnly, TextWrapping = readOnly ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = readOnly ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(7), Margin = new Thickness(0, 3, 0, 7), MinHeight = 30 };
        private static TextBlock Label(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 4, 0, 8) };
        private StackPanel Page(string title) { var panel = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } }); return panel; }
        private Button Action(string text, Action action) {
            var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 3, 8, 7), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, __) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "本地工具箱", MessageBoxButton.OK, MessageBoxImage.Information); } }; actions.Add(button); return button;
        }
        private Button AsyncAction(string text, Func<Task> action) {
            var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 3, 8, 7), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += async (_, __) => {
                try { await action(); }
                catch (Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "本地工具箱", MessageBoxButton.OK, MessageBoxImage.Information); }
            };
            actions.Add(button); return button;
        }
        private static DataGrid Grid((string title, string path, double width)[] columns) {
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 5, 0, 5) };
            foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = c.title, Binding = new Binding(c.path), Width = c.width }); return grid;
        }
        private string? ChooseFolder() { using (var dialog = new Forms.FolderBrowserDialog { Description = "选择目录", ShowNewFolderButton = true }) return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null; }
        private WorldInfo SelectedWorld() => worldGrid.SelectedItem as WorldInfo ?? throw new InvalidOperationException("请先扫描并选择一个世界。");
        private static string SafeName(string value) { var clean = new string(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.'); return string.IsNullOrWhiteSpace(clean) ? "world" : clean; }
        private void SaveText(string name, string content) { var dialog = new SaveFileDialog { FileName = name, Filter = "文本文件|*.txt", OverwritePrompt = true }; if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false)); status.Text = "已保存报告，请分享前检查内容。"; } }
        private async Task Run(string name, Func<CancellationToken, IProgress<string>, Task> operation) {
            if (running != null) return;
            running = new CancellationTokenSource(); foreach (var button in actions) button.IsEnabled = false;
            root.IsReadOnly = source.IsReadOnly = destination.IsReadOnly = logPath.IsReadOnly = true; cancel.IsEnabled = true; status.Text = name;
            try { await operation(running.Token, new Progress<string>(message => status.Text = message)); }
            catch (OperationCanceledException) { status.Text = "操作已取消。源文件保持原位；如有 .partial- 输出，可在确认后自行清理。"; }
            catch (Exception ex) { status.Text = "操作未完成：" + ex.Message; MessageBox.Show(this, ex.Message + "\n\n没有覆盖原世界或原游戏目录。失败产生的 .partial- 输出不算正式备份。", "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { running.Dispose(); running = null; foreach (var button in actions) button.IsEnabled = true; root.IsReadOnly = source.IsReadOnly = destination.IsReadOnly = logPath.IsReadOnly = false; cancel.IsEnabled = false; }
        }
        private async Task Scan() {
            var path = root.Text;
            await Run("扫描世界…", async (ct, progress) => { var items = await Task.Run(() => WorldCatalog.Scan(path, ct, progress), ct); worlds.Clear(); foreach (var item in items) worlds.Add(item); status.Text = $"发现 {items.Count} 个世界。最后游玩时间来自 level.dat。"; });
        }
        private async Task ComputeSize() { var world = SelectedWorld(); await Run("计算大小…", async (ct, p) => { world.Bytes = await Task.Run(() => WorldCatalog.Size(world.Location, ct), ct); worldGrid.Items.Refresh(); status.Text = "所选世界大小：" + world.SizeText; }); }
        private async Task Backup() {
            var world = SelectedWorld(); var dialog = new SaveFileDialog { Filter = "ZIP 备份|*.zip", FileName = SafeName(new DirectoryInfo(world.Location).Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            await Run("备份世界…", async (ct, progress) => { var file = await Task.Run(() => Transfers.Backup(world.Location, dialog.FileName, ct, progress), ct); status.Text = "已备份并逐文件核验：" + file; });
        }
        private async Task VerifyBackup() {
            var dialog = new OpenFileDialog { Filter = "本工具箱备份|*.zip" }; if (dialog.ShowDialog(this) != true) return;
            await Run("校验备份…", async (ct, p) => { var manifest = await Task.Run(() => Transfers.VerifyBackup(dialog.FileName, ct), ct); status.Text = $"备份有效：{manifest.WorldName}，{manifest.Files.Count} 个文件，SHA-256 全部匹配。"; });
        }
        private async Task Restore() {
            var dialog = new OpenFileDialog { Filter = "本工具箱备份|*.zip" }; if (dialog.ShowDialog(this) != true) return;
            var parent = ChooseFolder(); if (parent == null) return;
            var target = Path.Combine(parent, SafeName(Path.GetFileNameWithoutExtension(dialog.FileName)) + "-restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (MessageBox.Show(this, "将备份恢复为新的世界目录：\n" + target + "\n\n请确认父目录是你需要的 saves 文件夹。", "恢复为新世界", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            await Run("恢复为新世界…", async (ct, p) => { await Task.Run(() => Transfers.RestoreAsNewWorld(dialog.FileName, target, ct, p), ct); status.Text = "已校验并恢复到：" + target; });
        }
        private void InvalidatePreview() { previewSource = previewDestination = copiedDestination = null; migrationPreview.Clear(); }
        private async Task PreviewMigration() {
            var from = source.Text; var to = destination.Text; FileSafety.DistinctTrees(from, to);
            await Run("整理复制计划…", async (ct, p) => {
                var files = await Task.Run(() => Transfers.MigrationPlan(from, ct), ct);
                previewSource = from; previewDestination = to;
                migrationPreview.Text = $"源：{from}\r\n目标：{to}\r\n{files.Count} 个文件，{files.Sum(f => f.Length) / 1073741824d:N2} GiB\r\n前 100 项：\r\n" + string.Join("\r\n", files.Take(100).Select(f => FileSafety.Relative(from, f.FullName)));
                status.Text = "复制计划已生成，请确认目标后开始。";
            });
        }
        private async Task Migrate() {
            var from = source.Text; var to = destination.Text;
            if (from != previewSource || to != previewDestination) throw new InvalidOperationException("请先预览当前源目录和目标目录。");
            if (MessageBox.Show(this, "请先退出源目录内的游戏世界，再开始复制。\n源：" + from + "\n目标：" + to, "复制并校验", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            await Run("复制游戏目录…", async (ct, progress) => { copiedDestination = await Task.Run(() => Transfers.Migrate(from, to, ct, progress), ct); status.Text = "复制与 SHA-256 校验完成。可点击“加入 PCL 文件夹列表”。"; });
        }
        private async Task CheckNetwork() => await Run("检查下载地址…", async (ct, p) => { network.Clear(); await DownloadDiagnostics.Run(ct, new Progress<NetworkResult>(r => network.Add(r))); status.Text = "本次下载诊断完成，请查看各项结果。"; });
        private async Task Analyze() {
            var path = logPath.Text; var gameRoot = root.Text;
            await Run("读取并分析日志…", async (ct, p) => { report = await Task.Run(() => { ct.ThrowIfCancellationRequested(); return LogDiagnostics.Report(LogDiagnostics.ReadTail(path), new[] { gameRoot, context.EffectiveDirectory }); }, ct); logReport.Text = report; status.Text = "日志分析完成；报告已尝试脱敏，导出分享前请复核。"; });
        }
    }
}
