using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PclLocalTools;

// Interactive acceptance harness: uses the shipped DLL, a private fixture directory,
// and a synthetic notice. It never creates a GitHub release or installs an update.
class NoticeHarness {
    [STAThread] static void Main() {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture");
        Directory.CreateDirectory(root);
        var app = new Application();
        var host = new Window { Title = "PCL 更新弹窗实机验收 — 测试数据", Width = 650, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(20) }; host.Content = panel;
        panel.Children.Add(new TextBlock { Text = "仅测试：v2.99.4 是人工数据，不是已发布版本。\n使用发布 DLL 的 UpdateNotice；测试设置与真实 PCL 隔离。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,20) });
        var result = new TextBlock { Margin = new Thickness(0,15,0,0), TextWrapping = TextWrapping.Wrap };
        var button = new Button { Content = "显示测试更新弹窗", Padding = new Thickness(10) };
        button.Click += delegate {
            var release = new UpdateRelease { Version = "v2.99.4", Body = "【实机验收测试数据】\n本版本不存在于 GitHub。请测试关闭、稍后和跳过；不要打开发布页。", Page = UpdateProtocol.ReleaseBase + "tag/v2.99.4" };
            var notice = new UpdateNotice(release, root, UpdateProtocol.LauncherName) { Owner = host };
            notice.Closed += delegate {
                string skipped = UpdateSettings.Read(root).SkippedVersion;
                string line = DateTime.UtcNow.ToString("o") + " closed; skipped=" + skipped + "; transactions=" + Directory.Exists(Path.Combine(root,"PCL","LocalUpdates"));
                result.Text = line; File.AppendAllText(Path.Combine(root, "events.txt"), line + Environment.NewLine);
            };
            notice.Show();
        };
        panel.Children.Add(button); panel.Children.Add(result); app.Run(host);
    }
}
