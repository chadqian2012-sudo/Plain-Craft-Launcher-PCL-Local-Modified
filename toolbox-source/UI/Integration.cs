using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PclLocalTools {
    public sealed class LauncherContext {
        public string Root { get; set; } = "";
        public string EffectiveDirectory { get; set; } = "";
        public string VersionDirectory { get; set; } = "";
        public List<string> KnownRoots { get; set; } = new List<string>();
        public Assembly? Launcher { get; set; }
    }
    public static class Integration {
        public const string Repository = "https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static object? Member(object? instance, string name) => instance == null ? null : instance.GetType().GetProperty(name, Flags)?.GetValue(instance) ?? instance.GetType().GetField(name, Flags)?.GetValue(instance);
        private static LauncherContext Context(Assembly? assembly) {
            var result = new LauncherContext { Launcher = assembly };
            if (assembly == null) return result;
            var mc = assembly.GetType("PCL.ModMinecraft");
            result.Root = Convert.ToString(mc?.GetProperty("McFolderSelected", Flags)?.GetValue(null)) ?? "";
            var folders = mc?.GetField("McFolderList", Flags)?.GetValue(null) as IEnumerable;
            if (folders != null) foreach (var folder in folders) {
                var location = Convert.ToString(Member(folder, "Location"));
                if (!string.IsNullOrWhiteSpace(location)) result.KnownRoots.Add(location!);
            }
            var instance = assembly.GetType("PCL.PageInstanceLeft")?.GetField("Instance", Flags)?.GetValue(null);
            if (instance != null) {
                result.VersionDirectory = Convert.ToString(Member(instance, "PathVersion")) ?? "";
                result.EffectiveDirectory = Convert.ToString(Member(instance, "PathIndie")) ?? "";
            }
            if (result.Root.Length != 0 && !result.KnownRoots.Contains(result.Root)) result.KnownRoots.Add(result.Root);
            return result;
        }
        public static void OpenRelease() {
            try { Process.Start(new ProcessStartInfo(Repository + "/releases") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("无法打开浏览器，请手动访问：\n" + Repository + "/releases\n\n" + ex.Message, "本修改版发布页"); }
        }
        public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        public static void OpenFolder(string folder) {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("目录尚不存在：" + folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetFullPath(folder) + "\"") { UseShellExecute = true });
        }
        public static void Show(Assembly? assembly = null, int tab = 0) {
            var window = new ToolboxWindow(Context(assembly), tab);
            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible && owner != window) window.Owner = owner;
            window.Show();
        }
        private static Panel? Panel(object page) => Member(page, "PanMain") as Panel;
        private static Border Card(string text, params Tuple<string, Action>[] actions) {
            var body = new StackPanel { Margin = new Thickness(14) };
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 0, 0, 8) });
            var buttons = new WrapPanel();
            foreach (var action in actions) {
                var button = new Button { Content = action.Item1, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 5) };
                button.Click += (_, __) => { try { action.Item2(); } catch (Exception ex) { MessageBox.Show(ex.Message, "本地工具箱"); } }; buttons.Children.Add(button);
            }
            body.Children.Add(buttons);
            return new Border { Tag = "PCL.LocalTools", Background = new SolidColorBrush(Color.FromRgb(237, 246, 255)), BorderBrush = new SolidColorBrush(Color.FromRgb(182, 211, 239)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 12, 0, 10), Child = body };
        }
        private static void Error(object page, Exception ex) {
            // Leave the original page functional if a host member changes in a future baseline.
            System.Diagnostics.Trace.WriteLine("PCL local tools attachment failed: " + ex.Message);
        }
        public static void AttachAbout(object page) {
            try {
                var panel = Panel(page); if (panel == null || panel.Children.OfType<Border>().Any(b => Equals(b.Tag, "PCL.LocalTools"))) return;
                var assembly = page.GetType().Assembly;
                panel.Children.Insert(0, Card("本地工具箱 2.99.2 · 存档备份、目录复制、下载诊断和日志分析。由你手动启动各项操作。",
                    Tuple.Create("打开本地工具箱", (Action)(() => Show(assembly))), Tuple.Create("本修改版发布页", (Action)OpenRelease)));
                var update = Member(page, "BtnAboutUpdate"); update?.GetType().GetProperty("Text")?.SetValue(update, "手动获取新版");
            } catch (Exception ex) { Error(page, ex); }
        }
        public static void AttachInstance(object page) {
            try {
                var panel = Panel(page); if (panel == null) return;
                foreach (var old in panel.Children.OfType<Border>().Where(b => Equals(b.Tag, "PCL.LocalTools")).ToArray()) panel.Children.Remove(old);
                var context = Context(page.GetType().Assembly);
                var effective = context.EffectiveDirectory; var saves = Path.Combine(effective, "saves");
                var isolated = effective.Length > 0 && context.VersionDirectory.Length > 0 && FileSafety.Full(effective).Equals(FileSafety.Full(context.VersionDirectory), StringComparison.OrdinalIgnoreCase);
                panel.Children.Insert(0, Card("实际游戏目录：" + effective + "\n版本隔离：" + (isolated ? "已启用" : "使用共享目录") + "\n存档目录：" + saves,
                    Tuple.Create("打开游戏目录", (Action)(() => OpenFolder(effective))),
                    Tuple.Create("打开存档目录", (Action)(() => OpenFolder(saves))),
                    Tuple.Create("存档备份与诊断", (Action)(() => Show(page.GetType().Assembly)))));
            } catch (Exception ex) { Error(page, ex); }
        }
        public static void AttachDownload(object page) {
            try {
                var grid = Member(page, "PanInner") as Grid; if (grid == null || grid.Children.OfType<Border>().Any(b => Equals(b.Tag, "PCL.LocalTools"))) return;
                if (grid.RowDefinitions.Count != 0) return;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var banner = Card("离线账号可以下载安装原版游戏；下载游戏文件仍需联网。失败时可检查实际下载地址，已有下载任务仍使用原来的重试与校验逻辑。",
                    Tuple.Create("下载诊断", (Action)(() => Show(page.GetType().Assembly, 2))));
                Grid.SetRow(banner, 0); grid.Children.Add(banner);
            } catch (Exception ex) { Error(page, ex); }
        }
        public static void RegisterFolder(LauncherContext context, string folder) {
            if (context.Launcher == null) throw new InvalidOperationException("请在 PCL 内打开工具箱后添加目录。");
            var settings = context.Launcher.GetType("PCL.Settings") ?? throw new InvalidOperationException("未找到 PCL 设置接口。");
            var get = settings.GetMethod("GetSafe", Flags)!; var set = settings.GetMethod("SetSafe", Flags)!;
            var value = Convert.ToString(get.Invoke(null, new object?[] { "LaunchFolders", null })) ?? "";
            var full = FileSafety.Full(folder) + Path.DirectorySeparatorChar;
            if (value.Split('|').Any(s => s.Contains(">") && FileSafety.Full(s.Substring(s.IndexOf('>') + 1).Replace("$", AppDomain.CurrentDomain.BaseDirectory)).Equals(FileSafety.Full(full), StringComparison.OrdinalIgnoreCase))) return;
            var entry = new DirectoryInfo(folder).Name.Replace(">", "_").Replace("|", "_") + ">" + full;
            var updated = value.Length == 0 ? entry : value + "|" + entry;
            set.Invoke(null, new object?[] { "LaunchFolders", updated, null });
            if (Convert.ToString(get.Invoke(null, new object?[] { "LaunchFolders", null })) != updated) throw new IOException("目录设置未成功保存。");
            var mc = context.Launcher.GetType("PCL.ModMinecraft"); var list = mc?.GetField("McFolderList", Flags)?.GetValue(null) as IList;
            var type = context.Launcher.GetType("PCL.ModMinecraft+McFolder");
            if (type != null && list != null) {
                var item = Activator.CreateInstance(type)!; type.GetField("Name")!.SetValue(item, new DirectoryInfo(folder).Name);
                type.GetField("Location")!.SetValue(item, full); var field = type.GetField("Type")!; field.SetValue(item, Enum.ToObject(field.FieldType, 2)); list.Add(item);
            }
        }
    }
}
