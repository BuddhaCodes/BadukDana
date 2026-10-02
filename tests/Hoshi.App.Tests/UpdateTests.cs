using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Hoshi.App.Services;
using Hoshi.App.Services.Updates;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Tests;

public sealed class UpdatePlatformTests
{
    private const string ReleaseJson = """
        {"tag_name":"v0.1.7","html_url":"https://github.com/BuddhaCodes/BadukDana/releases/tag/v0.1.7","draft":false,"prerelease":false,
         "body":"Notes","assets":[
          {"name":"Hoshi-windows-x64.zip","size":1234,"browser_download_url":"https://github.com/BuddhaCodes/BadukDana/releases/download/v0.1.7/Hoshi-windows-x64.zip"},
          {"name":"SHA256SUMS.txt","size":99,"browser_download_url":"https://github.com/BuddhaCodes/BadukDana/releases/download/v0.1.7/SHA256SUMS.txt"}]}
        """;

    [Fact]
    public void Reads_a_github_release()
    {
        ReleaseInfo r = GitHubReleaseSource.Parse(ReleaseJson)!;

        r.Version.Should().Be(new Version(0, 1, 7));
        r.Tag.Should().Be("v0.1.7");
        r.Asset("hoshi-windows-x64.zip")!.Size.Should().Be(1234);
        r.Asset(UpdateService.ChecksumsAsset).Should().NotBeNull();
        r.Notes.Should().Be("Notes");
        GitHubReleaseSource.Parse(ReleaseJson.Replace("\"prerelease\":false", "\"prerelease\":true", StringComparison.Ordinal)).Should().BeNull();
        GitHubReleaseSource.Parse("""{"tag_name":"nightly"}""").Should().BeNull();
    }

    [Theory]
    [InlineData("v0.1.7", "0.1.7")]
    [InlineData("0.1.7+3f2a9c1", "0.1.7")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("V1.0.12-beta", "1.0.12")]
    public void Versions_are_read_loosely(string text, string expected) =>
        UpdatePlatform.ParseVersion(text).Should().Be(Version.Parse(expected));

    [Fact]
    public void Each_system_gets_its_own_file()
    {
        UpdatePlatform.AssetName(OSPlatform.Windows, Architecture.X64).Should().Be("Hoshi-windows-x64.zip");
        UpdatePlatform.AssetName(OSPlatform.OSX, Architecture.Arm64).Should().Be("Hoshi-macos-arm64.zip");
        UpdatePlatform.AssetName(OSPlatform.Linux, Architecture.Arm64).Should().Be("Hoshi-linux-arm64.tar.gz");
        UpdatePlatform.AssetName(OSPlatform.Linux, Architecture.X86).Should().BeNull();
    }

    [Fact]
    public void Checksums_are_found_by_file_name()
    {
        string hash = new('a', 64);
        string listing = $"{new string('b', 64)}  Hoshi-linux-x64.tar.gz\n{hash}  Hoshi-windows-x64.zip\n";
        UpdatePlatform.ChecksumFor(listing, "Hoshi-windows-x64.zip").Should().Be(hash);
        UpdatePlatform.ChecksumFor(listing, "Hoshi-macos-x64.zip").Should().BeNull();
    }

    [Fact]
    public void The_installation_kind_comes_from_the_executable()
    {
        using var dir = new TempDir();
        string exe = Path.Combine(dir.Path, "Hoshi.exe");
        File.WriteAllText(exe, "x");

        UpdatePlatform.Detect(exe, dir.Path, OSPlatform.Windows, Architecture.X64).Kind.Should().Be(InstallKind.Folder);
        UpdatePlatform.Detect("/usr/bin/dotnet", dir.Path, OSPlatform.Linux, Architecture.X64).Kind.Should().Be(InstallKind.Development);

        File.WriteAllText(Path.Combine(dir.Path, "Hoshi.dll"), "x");
        UpdatePlatform.Detect(exe, dir.Path, OSPlatform.Windows, Architecture.X64).Kind.Should().Be(InstallKind.Development, "a framework-dependent build sits next to Hoshi.dll");

        InstallTarget mac = UpdatePlatform.Detect("/Applications/Hoshi.app/Contents/MacOS/Hoshi", "/Applications/Hoshi.app/Contents/MacOS/", OSPlatform.OSX, Architecture.Arm64);
        mac.Kind.Should().Be(InstallKind.MacBundle);
        mac.Root.Should().Be("/Applications/Hoshi.app");
        mac.AssetName.Should().Be("Hoshi-macos-arm64.zip");
    }
}

public sealed class UpdateInstallerTests
{
    [Fact]
    public void A_tar_update_replaces_the_files_keeps_the_old_ones_aside_and_cleans_up_later()
    {
        using var dir = new TempDir();
        string install = Directory.CreateDirectory(Path.Combine(dir.Path, "Hoshi")).FullName;
        File.WriteAllText(Path.Combine(install, "Hoshi"), "old exe");
        File.WriteAllText(Path.Combine(install, "appsettings.json"), "old");
        string archive = Path.Combine(dir.Path, "Hoshi-linux-x64.tar.gz");
        WriteTarGz(archive, ("Hoshi/Hoshi", "new exe"), ("Hoshi/appsettings.json", "new"), ("Hoshi/licenses/OFL.txt", "license"));
        var target = new InstallTarget(InstallKind.Folder, install, Path.Combine(install, "Hoshi"), "Hoshi-linux-x64.tar.gz");

        string launched = UpdateInstaller.Install(archive, target);

        launched.Should().Be(Path.Combine(install, "Hoshi"));
        File.ReadAllText(launched).Should().Be("new exe");
        File.ReadAllText(Path.Combine(install, "appsettings.json")).Should().Be("new");
        File.ReadAllText(Path.Combine(install, "licenses", "OFL.txt")).Should().Be("license");
        File.ReadAllText(Path.Combine(install, "Hoshi.old")).Should().Be("old exe");
        Directory.Exists(Path.Combine(install, UpdateInstaller.StagingName)).Should().BeFalse();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(launched).Should().HaveFlag(UnixFileMode.UserExecute);
        }

        UpdateInstaller.CleanUp(target);
        Directory.EnumerateFiles(install, "*.old", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void A_zip_update_works_the_same_and_a_broken_one_changes_nothing()
    {
        using var dir = new TempDir();
        string install = Directory.CreateDirectory(Path.Combine(dir.Path, "app")).FullName;
        File.WriteAllText(Path.Combine(install, "Hoshi.exe"), "old");
        var target = new InstallTarget(InstallKind.Folder, install, Path.Combine(install, "Hoshi.exe"), "Hoshi-windows-x64.zip");

        string broken = Path.Combine(dir.Path, "broken.zip");
        WriteZip(broken, ("Hoshi/readme.txt", "no exe"));
        FluentActions.Invoking(() => UpdateInstaller.Install(broken, target)).Should().Throw<InvalidDataException>();
        File.ReadAllText(Path.Combine(install, "Hoshi.exe")).Should().Be("old");

        string good = Path.Combine(dir.Path, "Hoshi-windows-x64.zip");
        WriteZip(good, ("Hoshi/Hoshi.exe", "new"));
        File.ReadAllText(UpdateInstaller.Install(good, target)).Should().Be("new");
    }

    [Fact]
    public void Development_builds_never_install()
    {
        var dev = new InstallTarget(InstallKind.Development, "/x", "/usr/bin/dotnet", "Hoshi-linux-x64.tar.gz");
        UpdateInstaller.CanWrite(dev).Should().BeFalse();
        FluentActions.Invoking(() => UpdateInstaller.Install("a.zip", dev)).Should().Throw<InvalidOperationException>();
    }

    internal static void WriteTarGz(string path, params (string Name, string Text)[] entries)
    {
        using FileStream file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip);
        foreach ((string name, string text) in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            };
            tar.WriteEntry(entry);
        }
    }

    internal static void WriteZip(string path, params (string Name, string Text)[] entries)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string text) in entries)
        {
            using StreamWriter w = new(zip.CreateEntry(name).Open());
            w.Write(text);
        }
    }
}

public sealed class UpdateServiceTests
{
    private static readonly Uri AssetUrl = new("https://example.test/Hoshi-linux-x64.tar.gz");
    private static readonly Uri SumsUrl = new("https://example.test/SHA256SUMS.txt");

    [Fact]
    public async Task Only_a_newer_release_with_a_file_for_this_system_is_offered()
    {
        using var dir = new TempDir();
        UpdateService service = Service(dir, new Version(0, 1, 5), out _, Release("0.1.7"));
        (await service.CheckAsync(CancellationToken.None))!.Version.Should().Be(new Version(0, 1, 7));

        (await Service(dir, new Version(0, 1, 7), out _, Release("0.1.7")).CheckAsync(CancellationToken.None)).Should().BeNull();
        (await Service(dir, new Version(0, 1, 5), out _, Release("0.1.7", asset: "Hoshi-other.zip")).CheckAsync(CancellationToken.None)).Should().BeNull();
        (await Service(dir, new Version(0, 1, 5), out _, null).CheckAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Downloads_are_checked_against_the_published_checksums()
    {
        using var dir = new TempDir();
        byte[] payload = Encoding.UTF8.GetBytes("the new hoshi");
        string hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        UpdateService service = Service(dir, new Version(0, 1, 5), out FakeHttp http, Release("0.1.7"));
        http.Responses[AssetUrl] = payload;
        http.Responses[SumsUrl] = Encoding.UTF8.GetBytes($"{hash}  Hoshi-linux-x64.tar.gz\n");
        var reports = new List<double>();

        string file = await service.DownloadAsync(Release("0.1.7")!, new SyncProgress(reports.Add), CancellationToken.None);

        File.ReadAllBytes(file).Should().Equal(payload);
        reports.Should().NotBeEmpty().And.Contain(1.0);

        http.Responses[SumsUrl] = Encoding.UTF8.GetBytes($"{new string('0', 64)}  Hoshi-linux-x64.tar.gz\n");
        await FluentActions.Invoking(() => service.DownloadAsync(Release("0.1.7")!, null, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void An_update_start_waits_only_for_a_real_previous_process()
    {
        UpdateService.WaitForPreviousInstance(["--wait-for-pid", "999999"]); // not running: returns at once
        UpdateService.WaitForPreviousInstance(["--other"]);
    }

    private static ReleaseInfo? Release(string version, string asset = "Hoshi-linux-x64.tar.gz") =>
        new(Version.Parse(version), "v" + version, new Uri("https://example.test/release"),
            [new ReleaseAsset(asset, AssetUrl, 13), new ReleaseAsset(UpdateService.ChecksumsAsset, SumsUrl, 80)], null);

    private static UpdateService Service(TempDir dir, Version current, out FakeHttp http, ReleaseInfo? latest)
    {
        http = new FakeHttp();
        var target = new InstallTarget(InstallKind.Folder, dir.Path, Path.Combine(dir.Path, "Hoshi"), "Hoshi-linux-x64.tar.gz");
        return new UpdateService(new FixedSource(latest), new HttpClient(http), target: target, current: current, downloadDirectory: Path.Combine(dir.Path, "dl"));
    }
}

public sealed class UpdateViewModelTests
{
    [Fact]
    public async Task A_newer_version_is_offered_and_can_be_skipped()
    {
        var settings = new TestSettings();
        var updates = new FakeUpdates { Latest = Release("0.1.9") };
        var vm = new UpdateViewModel(updates, settings);

        await vm.CheckAsync(CancellationToken.None);
        vm.IsVisible.Should().BeTrue();
        vm.Text.Should().Be("Hoshi 0.1.9 está disponible (tienes la 0.1.5).");

        vm.SkipCommand.Execute(null);
        vm.IsVisible.Should().BeFalse();
        settings.Current.SkippedUpdate.Should().Be("0.1.9");

        var again = new UpdateViewModel(updates, settings);
        await again.CheckAsync(CancellationToken.None);
        again.IsVisible.Should().BeFalse("that version was skipped");

        settings.Save(settings.Current with { CheckForUpdates = false, SkippedUpdate = null });
        await again.CheckAsync(CancellationToken.None);
        again.IsVisible.Should().BeFalse("automatic checks are off");
    }

    [Fact]
    public async Task Updating_installs_launches_and_closes_or_opens_the_page_when_it_cannot()
    {
        var updates = new FakeUpdates { Latest = Release("0.1.9") };
        var shutdown = new FakeShutdown();
        var vm = new UpdateViewModel(updates, new TestSettings(), shutdown: shutdown);
        await vm.CheckAsync(CancellationToken.None);
        vm.UpdateLabel.Should().Be("Actualizar y reiniciar");

        await vm.UpdateCommand.ExecuteAsync(null);

        updates.Launched.Should().Be("installed:downloaded");
        shutdown.Count.Should().Be(1);

        var browser = new FakeBrowser();
        var readOnly = new FakeUpdates { Latest = Release("0.1.9"), CanInstall = false };
        var vm2 = new UpdateViewModel(readOnly, new TestSettings(), browser);
        await vm2.CheckAsync(CancellationToken.None);
        vm2.UpdateLabel.Should().Be("Descargar");
        await vm2.UpdateCommand.ExecuteAsync(null);
        browser.Opened.Should().Equal(new Uri("https://example.test/release"));
        readOnly.Launched.Should().BeNull();
    }

    [Fact]
    public async Task A_manual_check_says_when_there_is_nothing_new()
    {
        var vm = new UpdateViewModel(new FakeUpdates(), new TestSettings());
        await vm.CheckNowCommand.ExecuteAsync(null);
        vm.IsVisible.Should().BeTrue();
        vm.StatusText.Should().Be("Tienes la última versión (0.1.5).");
        vm.LaterCommand.Execute(null);
        vm.IsVisible.Should().BeFalse();
    }

    private static ReleaseInfo Release(string v) =>
        new(Version.Parse(v), "v" + v, new Uri("https://example.test/release"), [new ReleaseAsset("Hoshi-linux-x64.tar.gz", new Uri("https://example.test/a"), 1)], null);
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("hoshi-update-test").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class FixedSource(ReleaseInfo? release) : IReleaseSource
{
    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(release);
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<Uri, byte[]> Responses { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Responses.TryGetValue(request.RequestUri!, out byte[]? body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}

internal sealed class SyncProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}

internal sealed class FakeUpdates : IUpdateService
{
    public ReleaseInfo? Latest { get; set; }

    public string? Launched { get; private set; }

    public InstallTarget Target { get; } = new(InstallKind.Folder, "/x", "/x/Hoshi", "Hoshi-linux-x64.tar.gz");

    public Version CurrentVersion { get; } = new(0, 1, 5);

    public bool CanInstall { get; set; } = true;

    public Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Latest is { } l && l.Version > CurrentVersion ? l : null);

    public Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(1);
        return Task.FromResult("downloaded");
    }

    public string Install(string archive) => "installed:" + archive;

    public void Launch(string installed) => Launched = installed;
}

internal sealed class FakeShutdown : IAppShutdown
{
    public int Count { get; private set; }

    public void Shutdown() => Count++;
}

internal sealed class FakeBrowser : IBrowserLauncher
{
    public List<Uri> Opened { get; } = [];

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        Opened.Add(uri);
        return Task.CompletedTask;
    }
}
