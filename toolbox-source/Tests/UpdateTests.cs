using PclLocalTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

internal static class UpdateTests {
    private static readonly CancellationToken None = CancellationToken.None;
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is OperationCanceledException) { return true; } }
    private sealed class Fixture {
        public string Root = "", Archive = "", RequestPath = "";
        public UpdateRequest Request = new UpdateRequest();
        public readonly Dictionary<string, byte[]> Old = new Dictionary<string, byte[]>();
        public readonly Dictionary<string, byte[]> New = new Dictionary<string, byte[]>();
        public void Prepare() { UpdateInstaller.Prepare(Archive, Request.Manifest, Request.TransactionDirectory, None); UpdateProtocol.Save(RequestPath, Request); }
        public bool Original() => Old.All(p => File.Exists(Path.Combine(Root, p.Key)) && File.ReadAllBytes(Path.Combine(Root, p.Key)).SequenceEqual(p.Value));
    }
    private static Fixture Make(string parent, string? unsafeEntry = null, string? exeName = null) {
        var area = Path.Combine(parent, "updater-" + Guid.NewGuid().ToString("N")); var f = new Fixture { Root = Path.Combine(area, "launcher"), Archive = Path.Combine(area, "fixture-launcher.zip") };
        Directory.CreateDirectory(f.Root);
        f.Request = new UpdateRequest { TransactionId = Guid.NewGuid().ToString("N"), TargetDirectory = f.Root, TargetExeName = exeName ?? UpdateProtocol.LauncherName, ParentPid = int.MaxValue, ParentStartUtcTicks = DateTime.UtcNow.Ticks, Manifest = new UpdateManifest { Format = 1, Version = "2.99.4", ArchiveName = "fixture-launcher.zip" } };
        foreach (var name in UpdateProtocol.PayloadNames) {
            var target = name == UpdateProtocol.LauncherName ? f.Request.TargetExeName : name;
            var old = Encoding.UTF8.GetBytes("OLD-FIXTURE-" + name); var newer = Encoding.UTF8.GetBytes("NEW-FIXTURE-" + name);
            File.WriteAllBytes(Path.Combine(f.Root, target), old); f.Old[target] = old; f.New[name] = newer;
            f.Request.Manifest.Files.Add(new UpdateFile { Name = name, Bytes = newer.Length, Sha256 = Hash(newer) });
        }
        Directory.CreateDirectory(Path.Combine(f.Root, "PCL")); File.WriteAllText(Path.Combine(f.Root, "PCL", "Setup.ini"), "PRIVATE-FIXTURE-CONFIG");
        Directory.CreateDirectory(Path.Combine(f.Root, ".minecraft", "saves", "fixture-world")); File.WriteAllText(Path.Combine(f.Root, ".minecraft", "saves", "fixture-world", "level.dat"), "PRIVATE-FIXTURE-WORLD");
        using (var stream = File.Create(f.Archive)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) {
            foreach (var item in f.New) using (var output = zip.CreateEntry(item.Key).Open()) output.Write(item.Value, 0, item.Value.Length);
            using (var writer = new StreamWriter(zip.CreateEntry("使用说明.md").Open())) writer.Write("fixture documentation must not overwrite local files");
            if (unsafeEntry != null) using (var writer = new StreamWriter(zip.CreateEntry(unsafeEntry).Open())) writer.Write("UNSAFE-FIXTURE");
        }
        f.Request.Manifest.ArchiveSha256 = HashFile(f.Archive); f.RequestPath = Path.Combine(f.Request.TransactionDirectory, "request.json");
        return f;
    }
    private static byte[] Release(Fixture f, string tag = "v2.99.4", bool prerelease = false, bool draft = false, bool manifest = true) {
        var prefix = UpdateProtocol.ReleaseBase + "download/" + tag + "/";
        var manifestBytes = Encoding.UTF8.GetBytes(UpdateProtocol.Json().Serialize(f.Request.Manifest));
        var assets = new List<object> { new { name = f.Request.Manifest.ArchiveName, browser_download_url = prefix + f.Request.Manifest.ArchiveName, size = new FileInfo(f.Archive).Length, digest = "sha256:" + f.Request.Manifest.ArchiveSha256.ToLowerInvariant() } };
        if (manifest) assets.Add(new { name = "PCL-update.json", browser_download_url = prefix + "PCL-update.json", size = manifestBytes.Length, digest = "sha256:" + Hash(manifestBytes).ToLowerInvariant() });
        return Encoding.UTF8.GetBytes(UpdateProtocol.Json().Serialize(new { tag_name = tag, name = "Fixture release", body = "Fixture release notes", html_url = UpdateProtocol.ReleaseBase + "tag/" + tag, draft, prerelease, assets }));
    }
    private sealed class Handler : HttpMessageHandler {
        public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
        public int Calls; public bool NoAuthorization = true, WrongSource; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; NoAuthorization &= request.Headers.Authorization == null;
            if (WrongSource) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request, Headers = { Location = new Uri("https://example.test/untrusted.exe") } });
            return Task.FromResult(new HttpResponseMessage(Status) { RequestMessage = request, Content = new ByteArrayContent(Data[request.RequestUri.AbsoluteUri]) });
        }
    }
    private static void Journal(Fixture f, bool corruptBackup = false) {
        var backup = Path.Combine(f.Request.TransactionDirectory, "backup"); Directory.CreateDirectory(backup); var record = new UpdateJournal { State = "Applying" };
        foreach (var file in f.Request.Manifest.Files) {
            var name = f.Request.TargetName(file); File.WriteAllBytes(Path.Combine(backup, name), f.Old[name]);
            record.Files.Add(new UpdateBackup { Name = name, Existed = true, OldSha256 = Hash(f.Old[name]), NewSha256 = file.Sha256 });
        }
        if (corruptBackup) File.WriteAllText(Path.Combine(backup, f.Request.TargetExeName), "CORRUPTED");
        File.WriteAllBytes(Path.Combine(f.Root, f.Request.TargetExeName), f.New[UpdateProtocol.LauncherName]);
        UpdateProtocol.Save(Path.Combine(f.Request.TransactionDirectory, "journal.json"), record);
    }
    private static int Helper(Fixture fixture, string mode) {
        var built = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\UpdaterHost\bin\Release\net48\PCL Update Helper.exe"));
        var runner = Path.Combine(fixture.Request.TransactionDirectory, "runner"); Directory.CreateDirectory(runner);
        var copy = Path.Combine(runner, UpdateProtocol.HelperName); File.Copy(built, copy, true); File.Copy(built + ".config", copy + ".config", true);
        using (var process = Process.Start(new ProcessStartInfo(copy, mode + " \"" + fixture.RequestPath + "\" --quiet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) {
            if (process == null) throw new Exception("Fixture helper did not start.");
            if (!process.WaitForExit(15000)) { process.Kill(); throw new Exception("Fixture helper timed out."); }
            return process.ExitCode;
        }
    }
    public static List<Tuple<bool, string>> Run(string directory) {
        var results = new List<Tuple<bool, string>>(); void Add(bool value, string name) { if (!value) throw new Exception("Update check failed: " + name); results.Add(Tuple.Create(value, name)); }
        Add(UpdateProtocol.ParseVersion("v2.99.3") == UpdateProtocol.ParseVersion("2.99.3.0") && UpdateProtocol.ParseVersion("2.100.0") > UpdateProtocol.ParseVersion("2.99.99"), "update version comparisons are numeric and normalized");
        Add(Reject(() => UpdateProtocol.ParseVersion("v2.99.4-beta")), "prerelease tag cannot become a stable update");
        var f = Make(directory); var valid = Release(f);
        Add(UpdateRelease.Parse(valid, "2.99.3")?.Version == "v2.99.4", "only a newer release is selected");
        Add(UpdateRelease.Parse(valid, "2.99.4") == null && UpdateRelease.Parse(valid, "3.0.0") == null, "same or older release never triggers downgrade");
        Add(UpdateRelease.Parse(Release(f, prerelease: true), "2.99.3") == null && UpdateRelease.Parse(Release(f, draft: true), "2.99.3") == null, "drafts and prereleases are ignored");
        Add(UpdateRelease.Parse(Release(f, manifest: false), "2.99.3")?.SupportsInstall == false, "newer legacy release falls back to opening its release page");
        Add(!UpdateProtocol.AssetUrl("https://github.com/another/repo/releases/download/v1/file.zip") && !UpdateProtocol.AssetUrl("http://github.com/" + UpdateProtocol.Repository + "/releases/download/v1/file.zip"), "updater rejects foreign repository and plaintext sources");
        var settings = new UpdateSettings(); var now = DateTime.UtcNow;
        Add(settings.Enabled && settings.ShouldCheckAutomatically(now), "automatic check is enabled by default");
        settings.LastCheckUtcTicks = now.Ticks; Add(!settings.ShouldCheckAutomatically(now.AddMinutes(29)) && settings.ShouldCheckAutomatically(now.AddMinutes(30)), "startup request cache limits repeated checks to thirty minutes");
        settings.Enabled = false; Add(!settings.ShouldCheckAutomatically(now.AddHours(1)), "disabled automatic checking remains disabled");
        settings.SkippedVersion = "v2.99.4"; settings.Write(f.Root); var stored = UpdateSettings.Read(f.Root);
        Add(!stored.Enabled && stored.SkippedVersion == "v2.99.4" && File.ReadAllText(Path.Combine(f.Root, "PCL", "Setup.ini")) == "PRIVATE-FIXTURE-CONFIG", "update preferences persist separately from original launcher config");
        var handler = new Handler(); handler.Data[UpdateProtocol.Api] = valid;
        var release = UpdateRelease.Parse(valid, "2.99.3")!;
        handler.Data[release.Assets.Single(a => a.Name == "PCL-update.json").Url] = Encoding.UTF8.GetBytes(UpdateProtocol.Json().Serialize(f.Request.Manifest));
        handler.Data[release.Assets.Single(a => a.Name == f.Request.Manifest.ArchiveName).Url] = File.ReadAllBytes(f.Archive);
        using (var client = new GitHubUpdateClient(handler)) {
            var found = client.Check("2.99.3", None).GetAwaiter().GetResult()!;
            var manifest = client.Manifest(found, None).GetAwaiter().GetResult();
            var archive = client.Download(found, manifest, f.Request.TransactionDirectory, None).GetAwaiter().GetResult();
            Add(HashFile(archive) == f.Request.Manifest.ArchiveSha256 && handler.NoAuthorization && handler.Calls == 3, "mock GitHub check, manifest and archive download verify hashes without credentials");
        }
        var redirect = new Handler { WrongSource = true };
        using (var client = new GitHubUpdateClient(redirect)) Add(Reject(() => client.Check("2.99.3", None).GetAwaiter().GetResult()), "redirect outside GitHub asset hosts is rejected");
        var rate = new Handler { Status = HttpStatusCode.Forbidden }; rate.Data[UpdateProtocol.Api] = valid;
        using (var client = new GitHubUpdateClient(rate)) Add(Reject(() => client.Check("2.99.3", None).GetAwaiter().GetResult()), "rate-limited request fails without replacement");
        var cancel = new CancellationTokenSource(); cancel.Cancel(); var none = new Handler();
        using (var client = new GitHubUpdateClient(none)) Add(Reject(() => client.Check("2.99.3", cancel.Token).GetAwaiter().GetResult()) && none.Calls == 0, "cancelled check makes no HTTP request");
        var damaged = Make(directory); damaged.Request.Manifest.ArchiveSha256 = new string('0', 64);
        Add(Reject(() => damaged.Prepare()) && damaged.Original(), "wrong ZIP hash leaves original program intact");
        var zipSlip = Make(directory, "../escaped.exe"); Add(Reject(() => zipSlip.Prepare()) && zipSlip.Original() && !File.Exists(Path.Combine(zipSlip.Request.TransactionDirectory, "escaped.exe")), "ZIP traversal rejected before program replacement");
        var duplicates = Make(directory, "pcllocaltools.dll"); Add(Reject(() => duplicates.Prepare()) && duplicates.Original(), "case-insensitive ZIP collisions rejected");
        var wrongFile = Make(directory); wrongFile.Request.Manifest.Files[0].Name = "PCL/Setup.ini";
        Add(Reject(() => wrongFile.Prepare()) && wrongFile.Original(), "manifest cannot overwrite game or launcher configuration");
        var tampered = Make(directory); tampered.Prepare(); File.WriteAllText(Path.Combine(tampered.Request.TransactionDirectory, "payload", UpdateProtocol.LauncherName), "TAMPERED");
        Add(Reject(() => UpdateInstaller.Apply(tampered.RequestPath)) && tampered.Original(), "post-download tampering rejected before any replacement");
        var locked = Make(directory); locked.Prepare(); using (File.Open(Path.Combine(locked.Root, locked.Request.TargetExeName), FileMode.Open, FileAccess.Read, FileShare.None)) Add(Reject(() => UpdateInstaller.Apply(locked.RequestPath)), "occupied launcher is never forcibly replaced");
        Add(locked.Original(), "occupied launcher failure preserves every original component");
        var cancelled = Make(directory); cancelled.Prepare(); cancelled.Request.ParentPid = Process.GetCurrentProcess().Id; cancelled.Request.ParentStartUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks; UpdateProtocol.Save(cancelled.RequestPath, cancelled.Request); File.WriteAllText(Path.Combine(cancelled.Request.TransactionDirectory, "cancel.flag"), "cancel");
        Add(Reject(() => UpdateInstaller.Apply(cancelled.RequestPath)) && cancelled.Original(), "cancelled installation does not terminate or replace the live parent");
        var success = Make(directory, exeName: "User Launcher.exe"); success.Prepare(); UpdateInstaller.Apply(success.RequestPath);
        Add(success.Request.Manifest.Files.All(file => HashFile(Path.Combine(success.Root, success.Request.TargetName(file))) == file.Sha256), "update installs all six components and preserves a renamed launcher path");
        Add(File.ReadAllText(Path.Combine(success.Root, "PCL", "Setup.ini")) == "PRIVATE-FIXTURE-CONFIG" && File.ReadAllText(Path.Combine(success.Root, ".minecraft", "saves", "fixture-world", "level.dat")) == "PRIVATE-FIXTURE-WORLD", "successful update never touches fixture config or world data");
        Add(success.Old.All(p => File.ReadAllBytes(Path.Combine(success.Request.TransactionDirectory, "backup", p.Key)).SequenceEqual(p.Value)), "successful update retains verified old program backups");
        var failed = Make(directory); failed.Prepare(); Add(Reject(() => UpdateInstaller.ApplyInternal(failed.RequestPath, false, index => { if (index == 1) throw new IOException("injected failure"); })) && failed.Original(), "failure after first replacement rolls back every original file");
        var created = Make(directory); File.Delete(Path.Combine(created.Root, "PCL Local Tools.exe")); created.Prepare();
        Add(Reject(() => UpdateInstaller.ApplyInternal(created.RequestPath, false, index => { if (index == 3) throw new IOException("injected failure"); })) && !File.Exists(Path.Combine(created.Root, "PCL Local Tools.exe")), "rollback removes only verified newly-created program components");
        var recovery = Make(directory); recovery.Prepare(); Journal(recovery); UpdateInstaller.Restore(recovery.RequestPath); Add(recovery.Original(), "journal recovery restores a simulated interrupted update");
        var badBackup = Make(directory); badBackup.Prepare(); Journal(badBackup, true);
        Add(Reject(() => UpdateInstaller.Restore(badBackup.RequestPath)) && HashFile(Path.Combine(badBackup.Root, badBackup.Request.TargetExeName)) == badBackup.Request.Manifest.Files[0].Sha256, "damaged recovery backup rejected before any target changes");
        var elsewhere = Make(directory); elsewhere.Prepare(); var fakeRequest = Path.Combine(directory, "outside-request.json"); UpdateProtocol.Save(fakeRequest, elsewhere.Request);
        Add(Reject(() => UpdateInstaller.Apply(fakeRequest)) && elsewhere.Original(), "helper rejects requests outside the owning transaction directory");
        Add(!UpdateNotice.CloseWasCancelled(true, false) && UpdateNotice.CloseWasCancelled(true, true) && !UpdateNotice.CloseWasCancelled(false, false), "PCL asynchronous exit animation is distinguished from a cancelled close");
        var isolatedHelper = Make(directory); isolatedHelper.Prepare();
        Add(Helper(isolatedHelper, "--apply") == 0 && isolatedHelper.Request.Manifest.Files.All(file => HashFile(Path.Combine(isolatedHelper.Root, file.Name)) == file.Sha256), "standalone helper process installs fixture components without a companion DLL");
        var realRecovery = Make(directory); realRecovery.Prepare(); Journal(realRecovery);
        Add(Helper(realRecovery, "--rollback") == 0 && realRecovery.Original(), "standalone helper process recovers an interrupted fixture update");
        var realCancel = Make(directory); realCancel.Prepare(); realCancel.Request.ParentPid = Process.GetCurrentProcess().Id; realCancel.Request.ParentStartUtcTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks; UpdateProtocol.Save(realCancel.RequestPath, realCancel.Request); File.WriteAllText(Path.Combine(realCancel.Request.TransactionDirectory, "cancel.flag"), "cancel");
        Add(Helper(realCancel, "--apply") != 0 && realCancel.Original() && !Process.GetCurrentProcess().HasExited, "standalone helper honors cancellation without killing its live fixture parent");
        var noticeRoot = Make(directory); var before = Directory.GetFiles(noticeRoot.Root, "*", SearchOption.AllDirectories).Length;
        var window = new UpdateNotice(release, noticeRoot.Root, UpdateProtocol.LauncherName);
        Add(!window.IsVisible && !window.Topmost, "update prompt is non-topmost and does not launch or install on construction");
        var content = (FrameworkElement)window.Content; window.Content = null; var surface = new Border { Background = window.Background, Child = content };
        surface.Measure(new Size(590, 470)); surface.Arrange(new Rect(0, 0, 590, 470)); surface.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(590, 470, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(directory, "update-notice.png"))) png.Save(file);
        window.Close();
        Add(Directory.GetFiles(noticeRoot.Root, "*", SearchOption.AllDirectories).Length == before && noticeRoot.Original(), "closing update prompt performs no download or installation");
        return results;
    }
}
