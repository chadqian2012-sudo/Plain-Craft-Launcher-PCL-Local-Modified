using PclLocalTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

internal static class Program {
    private static readonly CancellationToken None = CancellationToken.None;
    private static int count;
    private static readonly List<string> evidence = new List<string>();
    private static void Check(bool result, string label) { if (!result) throw new Exception("FAIL: " + label); count++; evidence.Add("PASS: " + label); Console.WriteLine(evidence.Last()); }
    private static void Rejected(Action action, string label) { try { action(); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is OperationCanceledException) { Check(true, label); return; } throw new Exception("NOT REJECTED: " + label); }
    private static void NbtText(Stream s, string text) { var b = Encoding.UTF8.GetBytes(text); s.WriteByte((byte)(b.Length >> 8)); s.WriteByte((byte)b.Length); s.Write(b, 0, b.Length); }
    private static void Level(string directory) {
        Directory.CreateDirectory(directory);
        using (var file = File.Create(Path.Combine(directory, "level.dat")))
        using (var s = new GZipStream(file, CompressionMode.Compress)) {
            s.WriteByte(10); NbtText(s, ""); s.WriteByte(10); NbtText(s, "Data");
            s.WriteByte(8); NbtText(s, "LevelName"); NbtText(s, "人工测试世界");
            s.WriteByte(4); NbtText(s, "LastPlayed"); long time = 1792800000000L; for (int shift = 56; shift >= 0; shift -= 8) s.WriteByte((byte)(time >> shift));
            s.WriteByte(10); NbtText(s, "Version"); s.WriteByte(8); NbtText(s, "Name"); NbtText(s, "26.3"); s.WriteByte(0); s.WriteByte(0); s.WriteByte(0);
        }
        File.WriteAllText(Path.Combine(directory, "session.lock"), "test-lock");
        Directory.CreateDirectory(Path.Combine(directory, "region")); File.WriteAllBytes(Path.Combine(directory, "region", "r.0.0.mca"), Enumerable.Range(0, 8192).Select(x => (byte)x).ToArray());
    }
    private static void BadZip(string path, string entryPath, bool badHash = false) {
        using (var stream = File.Create(path)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) {
            using (var writer = new StreamWriter(zip.CreateEntry(entryPath).Open())) writer.Write("x");
            var manifest = new BackupManifest { WorldName = "untrusted", Files = new List<BackupFile> { new BackupFile { Path = entryPath, Bytes = 1, Sha256 = new string('0', 64), ModifiedUtc = DateTime.UtcNow } } };
            using (var writer = new StreamWriter(zip.CreateEntry(Transfers.ManifestName).Open())) writer.Write(new JavaScriptSerializer().Serialize(manifest));
        }
    }
    [STAThread]
    private static int Main(string[] args) {
        string testRoot = Path.GetFullPath(args[0]); Directory.CreateDirectory(testRoot);
        var run = Path.Combine(testRoot, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(run);
        try {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var root = Path.Combine(run, "source", ".minecraft"); var world = Path.Combine(root, "saves", "world-测试"); Level(world);
            var isolated = Path.Combine(root, "versions", "26.3-Fabric", "saves", "isolated"); Level(isolated);
            var meta = LevelMetadata.Read(Path.Combine(world, "level.dat")); Check(meta.Name == "人工测试世界" && meta.Version == "26.3" && meta.LastPlayed.HasValue, "bounded NBT reader handles Chinese name, version and LastPlayed");
            var worlds = WorldCatalog.Scan(root, None); Check(worlds.Count == 2 && worlds.All(w => w.LastPlayed.HasValue), "shared and isolated saves discovered");
            Check(worlds.All(w => w.Bytes == -1) && WorldCatalog.Size(world, None) > 8192, "world scan avoids automatic deep sizing; explicit sizing works");
            var zip = Path.Combine(run, "world.zip");
            using (var locked = FileSafety.LockWorld(world)) Rejected(() => Transfers.Backup(world, zip, None), "active world backup rejected");
            Check(!File.Exists(zip), "failed backup not committed");
            var before = File.ReadAllBytes(Path.Combine(world, "level.dat")); Transfers.Backup(world, zip, None);
            var manifest = Transfers.VerifyBackup(zip, None); Check(manifest.Files.Count == 2 && manifest.Files.All(f => f.Path != "session.lock"), "backup hashes every file and excludes the session lock");
            var restored = Path.Combine(run, "restore-saves", "new-world"); Transfers.RestoreAsNewWorld(zip, restored, None);
            Check(File.ReadAllBytes(Path.Combine(restored, "level.dat")).SequenceEqual(before) && File.ReadAllBytes(Path.Combine(restored, "region", "r.0.0.mca")).Length == 8192, "restore-as-new preserves verified world content");
            Check(File.ReadAllBytes(Path.Combine(world, "level.dat")).SequenceEqual(before), "original world content unchanged");
            Rejected(() => Transfers.RestoreAsNewWorld(zip, world, None), "restore cannot overwrite existing world");
            Rejected(() => Transfers.Backup(world, Path.Combine(world, "recursive.zip"), None), "backup cannot contain itself");
            var malformed = Path.Combine(run, "traversal.zip"); BadZip(malformed, "../outside.txt");
            Rejected(() => Transfers.RestoreAsNewWorld(malformed, Path.Combine(run, "unsafe"), None), "ZIP traversal rejected before extraction");
            Check(!File.Exists(Path.Combine(run, "outside.txt")) && !Directory.Exists(Path.Combine(run, "unsafe")), "ZIP traversal writes no destination or escaped file");
            var corrupt = Path.Combine(run, "corrupt.zip"); BadZip(corrupt, "level.dat", true);
            Rejected(() => Transfers.VerifyBackup(corrupt, None), "corrupt file hash rejected");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Rejected(() => Transfers.Backup(world, Path.Combine(run, "cancelled.zip"), cancelled.Token), "cancelled backup rejected");
            Check(!File.Exists(Path.Combine(run, "cancelled.zip")), "cancelled backup never becomes final ZIP");
            Directory.CreateDirectory(Path.Combine(root, "mods")); File.WriteAllText(Path.Combine(root, "mods", "fixture.jar"), "test-fixture-not-a-real-mod");
            File.WriteAllText(Path.Combine(root, "launcher_accounts.json"), "SECRET-FIXTURE"); Directory.CreateDirectory(Path.Combine(root, "logs")); File.WriteAllText(Path.Combine(root, "logs", "latest.log"), "SECRET-FIXTURE");
            var migrated = Path.Combine(run, "copied-game");
            using (FileSafety.LockWorld(isolated)) Rejected(() => Transfers.Migrate(root, migrated, None), "migration rejected while isolated world is active");
            Check(!Directory.Exists(migrated), "active-world migration creates no completed directory");
            Transfers.Migrate(root, migrated, None);
            Check(File.Exists(Path.Combine(migrated, "mods", "fixture.jar")) && File.Exists(Path.Combine(migrated, "saves", "world-测试", "level.dat")), "migration copies game and world data");
            Check(!File.Exists(Path.Combine(migrated, "launcher_accounts.json")) && !Directory.Exists(Path.Combine(migrated, "logs")), "migration excludes account files and logs");
            Check(File.Exists(Path.Combine(root, "mods", "fixture.jar")) && File.Exists(Path.Combine(migrated, ".pcl-migration-manifest.json")), "migration preserves source and writes hash manifest");
            Rejected(() => Transfers.Migrate(root, Path.Combine(root, "nested"), None), "nested migration target rejected");
            Rejected(() => Transfers.Migrate(root, migrated, None), "existing migration target rejected");
            var log = "UnsupportedClassVersionError\n--accessToken command-secret\n{\"accessToken\":\"json-secret\"}\nAuthorization: Bearer bearer-secret\nrefresh_token=query-secret&x=1\nC:\\Users\\FixturePerson\\file.log\nfixture@example.test\n" + root;
            var report = LogDiagnostics.Report(log, new[] { root });
            Check(!new[] { "command-secret", "json-secret", "bearer-secret", "query-secret", "FixturePerson", "fixture@example.test", root }.Any(secret => report.Contains(secret)), "diagnostic export redacts fixture tokens, email and local paths");
            Check(report.Contains("Java 版本可能过低") && LogDiagnostics.Advice("checksum hash mismatch").Contains("校验值不匹配"), "diagnostic advice classifies Java and checksum failures");
            Check(LogDiagnostics.ExplainError("获取中……") == "获取中……", "unrecognized loader messages unchanged");
            Check(!LogDiagnostics.Redact("{\"password\":\"long secret with spaces\"}").Contains("secret with spaces"), "quoted secret values are fully redacted");
            var largeLog = Path.Combine(run, "large.log"); File.WriteAllText(largeLog, new string('x', 3 * 1024 * 1024) + "TAIL", new UTF8Encoding(false));
            var tail = LogDiagnostics.ReadTail(largeLog); Check(tail.Length <= 2 * 1024 * 1024 && tail.EndsWith("TAIL"), "log tail is bounded to two MiB");
            foreach (var result in NetworkTests.Run()) Check(result.Item1, result.Item2);
            foreach (var invalid in new[] { "../escape", "/absolute", "C:/escape", "a/../b", "CON.txt", "file:stream", "name.", "a//b" }) Rejected(() => FileSafety.SafeRelative(invalid), "reject unsafe entry " + invalid);
            PCL.ModMinecraft.McFolderSelected = root; PCL.PageInstanceLeft.Instance = new PCL.FakeInstance { PathVersion = Path.Combine(root, "versions", "26.3-Fabric"), PathIndie = Path.Combine(root, "versions", "26.3-Fabric") };
            var page = new PCL.FakePage(); Integration.AttachAbout(page); Integration.AttachAbout(page); Check(page.PanMain.Children.Count == 1, "about-page integration idempotent");
            Integration.AttachInstance(page); Integration.AttachInstance(page); Check(page.PanMain.Children.Count == 1, "instance-page integration replaces stale paths without duplicate cards");
            Integration.AttachDownload(page); Integration.AttachDownload(page); Check(page.PanInner.RowDefinitions.Count == 2 && page.PanInner.Children.Count == 1, "download banner integration is idempotent");
            PCL.Settings.Value = "existing>D:\\FixtureExisting\\";
            var fakeContext = new LauncherContext { Launcher = Assembly.GetExecutingAssembly() }; Integration.RegisterFolder(fakeContext, migrated); Integration.RegisterFolder(fakeContext, migrated);
            Check(PCL.Settings.Value.StartsWith("existing>D:\\FixtureExisting\\|") && PCL.ModMinecraft.McFolderList.Count == 1, "explicit folder registration preserves existing entries and avoids duplicates");
            var window = new ToolboxWindow(new LauncherContext { Root = @"X:\Games\.minecraft", EffectiveDirectory = @"X:\Games\.minecraft\versions\26.3-Fabric" }); Check(window.Title.Contains("2.99.2") && !window.IsVisible, "toolbox constructs without displaying or running operations");
            var view = (FrameworkElement)window.Content; window.Content = null;
            var tabs = ((Panel)view).Children.OfType<TabControl>().Single();
            var surface = new Border { Background = window.Background, Child = view };
            for (int i = 0; i < tabs.Items.Count; i++) {
                tabs.SelectedIndex = i; surface.Measure(new Size(1040, 670)); surface.Arrange(new Rect(0, 0, 1040, 670)); surface.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1040, 670, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(testRoot, "toolbox-tab-" + i + ".png"))) png.Save(file);
            }
            Check(tabs.Items.Count == 5, "all five toolbox tabs render offscreen in software without showing a window"); window.Close();
            evidence.Add("No real launcher, game directory, account configuration or user world was opened. HTTP diagnostics used only in-memory mocks; no external download probe ran.");
            File.WriteAllLines(Path.Combine(testRoot, "latest-results.txt"), evidence.Concat(new[] { "Total passed: " + count, "Fixture directory: " + run }), Encoding.UTF8);
            Console.WriteLine("ALL PASS: " + count); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(testRoot, "latest-failure.txt"), ex.ToString()); return 1; }
    }
}
namespace PCL {
    public static class ModMinecraft {
        public static string McFolderSelected { get; set; } = ""; public static List<McFolder> McFolderList = new List<McFolder>();
        public sealed class McFolder { public string Name = ""; public string Location = ""; public FolderType Type; }
        public enum FolderType { Vanilla, RenamedVanilla, Custom }
    }
    public static class Settings { public static string Value = ""; public static object GetSafe(string key, object? instance = null) => Value; public static void SetSafe(string key, object value, object? instance = null) => Value = Convert.ToString(value)!; }
    public static class PageInstanceLeft { public static FakeInstance? Instance; }
    public sealed class FakeInstance { public string PathVersion { get; set; } = ""; public string PathIndie { get; set; } = ""; }
    public sealed class FakePage : FrameworkElement { public StackPanel PanMain { get; } = new StackPanel(); public System.Windows.Controls.Grid PanInner { get; } = new System.Windows.Controls.Grid(); public FakeButton BtnAboutUpdate { get; } = new FakeButton(); }
    public sealed class FakeButton { public string Text { get; set; } = ""; }
}
