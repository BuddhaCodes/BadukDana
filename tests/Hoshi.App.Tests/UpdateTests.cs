using Hoshi.App.Services;
using Hoshi.App.Services.Updates;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Tests;

public sealed class UpdatePlatformTests
{
    private const string ReleaseJson = """
        {"tag_name":"v0.1.7","html_url":"https://github.com/BuddhaCodes/BadukDana/releases/tag/v0.1.7","draft":false,"prerelease":false,
         "body":"Notes","assets":[
          {"name":"HoshiGo-win-x64-Setup.exe","size":1234,"browser_download_url":"https://github.com/BuddhaCodes/BadukDana/releases/download/v0.1.7/HoshiGo-win-x64-Setup.exe"}]}
        """;

    [Fact]
    public void Reads_a_github_release()
    {
        ReleaseInfo r = GitHubReleaseSource.Parse(ReleaseJson)!;

        r.Version.Should().Be(new Version(0, 1, 7));
        r.Tag.Should().Be("v0.1.7");
        r.Asset("hoshigo-win-x64-setup.exe")!.Size.Should().Be(1234);
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
}

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task An_installed_copy_updates_through_the_installer()
    {
        var updater = new FakeAppUpdater { Installed = new Version(0, 1, 5), Next = new Version(0, 1, 9) };
        var service = new UpdateService(updater, new FixedSource(null));

        service.CanInstall.Should().BeTrue();
        service.CurrentVersion.Should().Be(new Version(0, 1, 5));
        ReleaseInfo release = (await service.CheckAsync(CancellationToken.None))!;
        release.Version.Should().Be(new Version(0, 1, 9));
        release.Tag.Should().Be("v0.1.9");

        var reports = new List<double>();
        await service.DownloadAsync(release, new SyncProgress(reports.Add), CancellationToken.None);
        reports.Should().Equal(0.5, 1.0);
        updater.Downloaded.Should().Be(new Version(0, 1, 9));

        service.ApplyOnExitAndRestart(release);
        updater.Applied.Should().Be(new Version(0, 1, 9));
    }

    [Fact]
    public async Task Nothing_newer_means_no_offer()
    {
        var updater = new FakeAppUpdater { Installed = new Version(0, 1, 9), Next = null };
        (await new UpdateService(updater, new FixedSource(null)).CheckAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_copy_without_the_installer_only_hears_about_new_releases()
    {
        var updater = new FakeAppUpdater { Installed = null };
        var service = new UpdateService(updater, new FixedSource(Release("0.1.9")), current: new Version(0, 1, 5));

        service.CanInstall.Should().BeFalse();
        (await service.CheckAsync(CancellationToken.None))!.Version.Should().Be(new Version(0, 1, 9));
        (await new UpdateService(updater, new FixedSource(Release("0.1.5")), current: new Version(0, 1, 5)).CheckAsync(CancellationToken.None)).Should().BeNull();
        await FluentActions.Invoking(() => service.DownloadAsync(Release("0.1.9"), null, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>("there is nothing to download it with");
    }

    private static ReleaseInfo Release(string v) => new(Version.Parse(v), "v" + v, new Uri("https://example.test/release"), [], null);
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
        vm.InstallerHint.Should().BeNull("this copy updates itself");

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
    public async Task Updating_downloads_applies_on_exit_and_closes_or_opens_the_download_page()
    {
        var updates = new FakeUpdates { Latest = Release("0.1.9") };
        var shutdown = new FakeShutdown();
        var vm = new UpdateViewModel(updates, new TestSettings(), shutdown: shutdown);
        await vm.CheckAsync(CancellationToken.None);
        vm.UpdateLabel.Should().Be("Actualizar y reiniciar");

        await vm.UpdateCommand.ExecuteAsync(null);

        updates.Applied.Should().Be("v0.1.9");
        shutdown.Count.Should().Be(1, "Hoshi closes normally (saving the game) and the installer restarts it");

        var browser = new FakeBrowser();
        var portable = new FakeUpdates { Latest = Release("0.1.9"), CanInstall = false };
        var vm2 = new UpdateViewModel(portable, new TestSettings(), browser);
        await vm2.CheckAsync(CancellationToken.None);
        vm2.UpdateLabel.Should().Be("Descargar el instalador");
        vm2.InstallerHint.Should().NotBeNull();
        await vm2.UpdateCommand.ExecuteAsync(null);
        browser.Opened.Should().Equal(UpdateViewModel.DownloadPage);
        portable.Applied.Should().BeNull();
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

    [Fact]
    public void Thanks_appear_once_after_an_update_and_never_on_a_first_install()
    {
        var settings = new TestSettings();
        var first = new UpdateViewModel(new FakeUpdates(), settings);
        first.ShowThanks.Should().BeFalse("a first install is not an update");
        settings.Current.LastRunVersion.Should().Be("0.1.5");

        new UpdateViewModel(new FakeUpdates(), settings).ShowThanks.Should().BeFalse("same version again");

        settings.Save(settings.Current with { LastRunVersion = "0.1.3" });
        var updated = new UpdateViewModel(new FakeUpdates(), settings);
        updated.ShowThanks.Should().BeTrue();
        updated.ThanksText.Should().Be("Hoshi se actualizó a la 0.1.5.");
        settings.Current.LastRunVersion.Should().Be("0.1.5");
        new UpdateViewModel(new FakeUpdates(), settings).ShowThanks.Should().BeFalse("only once per version");

        updated.DismissThanksCommand.Execute(null);
        updated.ShowThanks.Should().BeFalse();
    }

    [Fact]
    public async Task Support_opens_the_paypal_page_and_closes_the_thanks()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { LastRunVersion = "0.1.0" });
        var browser = new FakeBrowser();
        var vm = new UpdateViewModel(new FakeUpdates(), settings, browser);
        vm.ShowThanks.Should().BeTrue();

        await vm.SupportCommand.ExecuteAsync(null);

        browser.Opened.Should().Equal(UpdateViewModel.DonationPage);
        vm.ShowThanks.Should().BeFalse();
    }

    [Fact]
    public async Task An_update_offer_takes_precedence_over_the_thanks()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { LastRunVersion = "0.1.0" });
        var vm = new UpdateViewModel(new FakeUpdates { Latest = Release("0.1.9") }, settings);
        vm.ShowThanks.Should().BeTrue();

        await vm.CheckAsync(CancellationToken.None);

        vm.IsVisible.Should().BeTrue();
        vm.ShowThanks.Should().BeFalse("the two banners share the corner");
    }

    private static ReleaseInfo Release(string v) => new(Version.Parse(v), "v" + v, new Uri("https://example.test/release"), [], null);
}

internal sealed class FixedSource(ReleaseInfo? release) : IReleaseSource
{
    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(release);
}

internal sealed class SyncProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}

internal sealed class FakeAppUpdater : IAppUpdater
{
    public Version? Installed { get; set; }

    public Version? Next { get; set; }

    public Version? Downloaded { get; private set; }

    public Version? Applied { get; private set; }

    public bool IsInstalled => Installed is not null;

    public Version? InstalledVersion => Installed;

    public Task<PendingUpdate?> CheckAsync() =>
        Task.FromResult(Next is { } n ? new PendingUpdate(n, "notes", "handle") : null);

    public Task DownloadAsync(PendingUpdate update, Action<int>? progress, CancellationToken cancellationToken)
    {
        progress?.Invoke(50);
        progress?.Invoke(100);
        Downloaded = update.Version;
        return Task.CompletedTask;
    }

    public void ApplyOnExitAndRestart(PendingUpdate update) => Applied = update.Version;
}

internal sealed class FakeUpdates : IUpdateService
{
    public ReleaseInfo? Latest { get; set; }

    public string? Applied { get; private set; }

    public Version CurrentVersion { get; } = new(0, 1, 5);

    public bool CanInstall { get; set; } = true;

    public Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Latest is { } l && l.Version > CurrentVersion ? l : null);

    public Task DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(1);
        return Task.CompletedTask;
    }

    public void ApplyOnExitAndRestart(ReleaseInfo release) => Applied = release.Tag;
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
