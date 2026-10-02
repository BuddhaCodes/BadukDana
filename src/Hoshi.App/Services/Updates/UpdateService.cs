using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack;
using Velopack.Sources;

namespace Hoshi.App.Services.Updates;

/// <summary>A newer version that can be downloaded and applied by <see cref="IAppUpdater"/>.</summary>
/// <param name="Handle">The updater's own description of it (Velopack's <c>UpdateInfo</c>).</param>
public sealed record PendingUpdate(Version Version, string? Notes, object Handle);

/// <summary>Installs updates for a copy of Hoshi installed by its installer (Velopack).</summary>
public interface IAppUpdater
{
    /// <summary>True when this copy was installed by Hoshi's installer (or is its portable build) and can update itself.</summary>
    bool IsInstalled { get; }

    /// <summary>The installed version (null when not installed).</summary>
    Version? InstalledVersion { get; }

    Task<PendingUpdate?> CheckAsync();

    /// <summary>Downloads the update (only the changes when possible); progress goes from 0 to 100.</summary>
    Task DownloadAsync(PendingUpdate update, Action<int>? progress, CancellationToken cancellationToken);

    /// <summary>Applies the downloaded update once Hoshi has closed, then starts the new version.</summary>
    void ApplyOnExitAndRestart(PendingUpdate update);
}

/// <summary>
/// Velopack: the installers (Windows Setup.exe, macOS .pkg, Linux .AppImage) and their updates come from the
/// GitHub releases of the repository; <c>HOSHI_UPDATE_FEED</c> (a folder or URL) overrides the feed for testing.
/// Each copy follows the channel it was built for (win-x64, osx-arm64, linux-x64…).
/// </summary>
public sealed class VelopackUpdater : IAppUpdater
{
    private readonly Lazy<UpdateManager?> _manager;

    public VelopackUpdater(UpdateManager? manager = null) => _manager = new Lazy<UpdateManager?>(() => manager ?? Create());

    /// <summary>Null when Velopack did not start this process (tests, the designer): then Hoshi is simply "not installed".</summary>
    private UpdateManager? Manager => _manager.Value;

    public bool IsInstalled => Manager?.IsInstalled ?? false;

    public Version? InstalledVersion => Manager?.CurrentVersion is { } v ? new Version(v.Major, v.Minor, v.Patch) : null;

    public async Task<PendingUpdate?> CheckAsync()
    {
        if (Manager is not { } manager || await manager.CheckForUpdatesAsync() is not { } info)
        {
            return null;
        }

        VelopackAsset target = info.TargetFullRelease;
        return new PendingUpdate(new Version(target.Version.Major, target.Version.Minor, target.Version.Patch), target.NotesMarkdown, info);
    }

    public Task DownloadAsync(PendingUpdate update, Action<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        UpdateManager manager = Manager ?? throw new InvalidOperationException("Hoshi was not installed with its installer.");
        return manager.DownloadUpdatesAsync((UpdateInfo)update.Handle, progress, cancellationToken);
    }

    public void ApplyOnExitAndRestart(PendingUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        UpdateManager manager = Manager ?? throw new InvalidOperationException("Hoshi was not installed with its installer.");
        manager.WaitExitThenApplyUpdates(((UpdateInfo)update.Handle).TargetFullRelease, silent: false, restart: true);
    }

    private static UpdateManager? Create()
    {
        try
        {
            return Environment.GetEnvironmentVariable("HOSHI_UPDATE_FEED") is { Length: > 0 } feed
                ? new UpdateManager(feed)
                : new UpdateManager(new GithubSource($"https://github.com/{GitHubReleaseSource.Repository}", accessToken: null, prerelease: false));
        }
        catch (InvalidOperationException)
        {
            return null; // VelopackApp.Run() was not called in this process
        }
    }
}

/// <summary>Checking for, downloading and installing new Hoshi releases.</summary>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    /// <summary>
    /// True when this copy updates itself (installed with Hoshi's installer). Otherwise (run from source, or the
    /// old .zip/.tar.gz downloads) a new version is only announced and "Update" opens the download page.
    /// </summary>
    bool CanInstall { get; }

    /// <summary>The newest release when it is newer than this build, else null.</summary>
    Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Arranges for the update to be applied when Hoshi closes, and for the new version to start.</summary>
    void ApplyOnExitAndRestart(ReleaseInfo release);
}

/// <summary>
/// Updates through <see cref="IAppUpdater"/> (Velopack) when Hoshi was installed with its installer; otherwise only
/// tells about new releases (read from GitHub), so the player can download the installer.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly IAppUpdater _updater;
    private readonly IReleaseSource _releases;
    private readonly ILogger _logger;
    private PendingUpdate? _pending;

    public UpdateService(IAppUpdater updater, IReleaseSource releases, ILogger<UpdateService>? logger = null, Version? current = null)
    {
        _updater = updater ?? throw new ArgumentNullException(nameof(updater));
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _logger = logger ?? (ILogger)NullLogger.Instance;
        CurrentVersion = current ?? updater.InstalledVersion ?? UpdatePlatform.CurrentVersion;
    }

    public Version CurrentVersion { get; }

    public bool CanInstall => _updater.IsInstalled;

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        if (_updater.IsInstalled)
        {
            _pending = await _updater.CheckAsync().WaitAsync(cancellationToken);
            if (_pending is null || _pending.Version <= CurrentVersion)
            {
                return null;
            }

            string tag = "v" + UpdatePlatform.Display(_pending.Version);
            _logger.LogInformation("Hoshi {Version} is available (this is {Current})", tag, UpdatePlatform.Display(CurrentVersion));
            return new ReleaseInfo(_pending.Version, tag, new Uri($"https://github.com/{GitHubReleaseSource.Repository}/releases/tag/{tag}"), [], _pending.Notes);
        }

        ReleaseInfo? latest = await _releases.GetLatestAsync(cancellationToken);
        return latest is not null && latest.Version > CurrentVersion ? latest : null;
    }

    public async Task DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        PendingUpdate pending = Pending(release);
        await _updater.DownloadAsync(pending, p => progress?.Report(Math.Clamp(p / 100.0, 0, 1)), cancellationToken);
        _logger.LogInformation("Downloaded Hoshi {Version}", release.Tag);
    }

    public void ApplyOnExitAndRestart(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _updater.ApplyOnExitAndRestart(Pending(release));
    }

    private PendingUpdate Pending(ReleaseInfo release) =>
        _pending is { } p && p.Version == release.Version
            ? p
            : throw new InvalidOperationException("Check for updates before downloading one.");
}
