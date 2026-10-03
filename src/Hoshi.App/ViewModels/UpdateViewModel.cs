using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Updates;
using Hoshi.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>Closes the application (the update restarts it).</summary>
public interface IAppShutdown
{
    void Shutdown();
}

public sealed class AvaloniaAppShutdown : IAppShutdown
{
    public void Shutdown() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
}

/// <summary>
/// The update banner. Shortly after start (and every 12 hours) looks for a newer release; when there is one, offers
/// it. "Update" downloads it (Velopack: only what changed when possible), closes Hoshi normally (the game is saved)
/// and the new version starts. A copy not installed with the installer (run from source, or the old .zip downloads)
/// opens the download page instead. Never during the player's live OGS game.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    /// <summary>Where the installers are offered (the website picks the visitor's system).</summary>
    public static readonly Uri DownloadPage = new("https://buddhacodes.github.io/BadukDana/#download");

    /// <summary>Where the player can support Hoshi (GitHub Sponsors).</summary>
    public static readonly Uri SponsorPage = new("https://github.com/sponsors/BuddhaCodes");

    private readonly IUpdateService _updates;
    private readonly ISettingsService? _settings;
    private readonly IBrowserLauncher? _browser;
    private readonly IAppShutdown? _shutdown;
    private readonly GameViewModel? _game;
    private readonly ILogger _logger;
    private CancellationTokenSource? _loop;
    private string? _statusKey;
    private object?[] _statusArgs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(Text), nameof(InstallerHint), nameof(ShowThanks))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private ReleaseInfo? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(ShowThanks))]
    private bool _isDismissed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(CheckNowCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    /// <summary>Hoshi was updated since it last ran: a short thank-you with the support link, once per version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThanksText), nameof(ShowThanks))]
    private Version? _updatedTo;

    public UpdateViewModel(
        IUpdateService updates,
        ISettingsService? settings = null,
        IBrowserLauncher? browser = null,
        IAppShutdown? shutdown = null,
        GameViewModel? game = null,
        ILogger<UpdateViewModel>? logger = null)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _settings = settings;
        _browser = browser;
        _shutdown = shutdown;
        _game = game;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        if (_game is not null)
        {
            _game.PropertyChanged += OnGamePropertyChanged;
        }

        NoteRun();
    }

    /// <summary>Shown once after an update; never on a first install.</summary>
    public bool ShowThanks => UpdatedTo is not null && !IsVisible;

    public string? ThanksText => UpdatedTo is { } v ? Tr.F("Support.Updated", UpdatePlatform.Display(v)) : null;

    /// <summary>The banner shows a newer release (or the result of a manual check) until dismissed.</summary>
    public bool IsVisible => !IsDismissed && (Available is not null || _statusKey is not null);

    public string CurrentVersionText => UpdatePlatform.Display(_updates.CurrentVersion);

    public string? Text => Available is { } r
        ? Tr.F("Update.Available", UpdatePlatform.Display(r.Version), CurrentVersionText)
        : null;

    public string? StatusText => _statusKey is null ? null : Tr.F(_statusKey, _statusArgs);

    /// <summary>Shown under the offer when this copy cannot update itself.</summary>
    public string? InstallerHint => Available is not null && !_updates.CanInstall ? Tr.T("Update.InstallerHint") : null;

    public bool HasStatus => StatusText is not null;

    /// <summary>A live OGS game of the player is in progress: installing (which restarts Hoshi) waits.</summary>
    public bool IsBlocked => _game?.Online is { IsPlayer: true, IsFinished: false };

    /// <summary>"Update" installs in place, or (when this copy cannot replace itself) opens the download page.</summary>
    public string UpdateLabel => Tr.T(_updates.CanInstall ? "Update.Install" : "Update.Download");

    /// <summary>Starts the periodic check (called once the main window is up).</summary>
    public void Start()
    {
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
    }

    public void Stop() => _loop?.Cancel();

    /// <summary>One automatic check (respects the preference and a skipped version).</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        AppSettings s = _settings?.Current ?? new AppSettings();
        if (!s.CheckForUpdates)
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            ReleaseInfo? release = await _updates.CheckAsync(timeout.Token);
            if (release is not null && UpdatePlatform.Display(release.Version) != s.SkippedUpdate)
            {
                Available = release;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _logger.LogInformation("Update check skipped: {Message}", ex.Message);
        }
    }

    /// <summary>Menu: "Check for updates…" (shows the answer even when there is nothing new).</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckNow()
    {
        IsBusy = true;
        IsDismissed = false;
        SetStatus("Update.Checking");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ReleaseInfo? release = await _updates.CheckAsync(timeout.Token);
            Available = release;
            SetStatus(release is null ? "Update.UpToDate" : null, CurrentVersionText);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            SetStatus("Update.CheckFailed");
            _logger.LogInformation("Update check failed: {Message}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsIdle() => !IsBusy;

    private bool CanUpdate() => Available is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task Update()
    {
        if (Available is not { } release)
        {
            return;
        }

        if (!_updates.CanInstall)
        {
            if (_browser is not null)
            {
                await _browser.OpenAsync(DownloadPage, CancellationToken.None);
            }

            return;
        }

        if (IsBlocked)
        {
            SetStatus("Update.AfterGame");
            return;
        }

        IsBusy = true;
        Progress = 0;
        SetStatus("Update.Downloading", 0);
        bool downloading = true;
        try
        {
            var progress = new Progress<double>(p =>
            {
                if (!downloading)
                {
                    return; // a late report after the download finished
                }

                Progress = p;
                SetStatus("Update.Downloading", (int)Math.Round(p * 100));
            });
            await _updates.DownloadAsync(release, progress, CancellationToken.None);
            downloading = false;
            SetStatus("Update.Restarting");
            _updates.ApplyOnExitAndRestart(release);
            _shutdown?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Update failed");
            SetStatus("Update.Failed", ex.Message);
        }
        finally
        {
            downloading = false;
            IsBusy = false;
        }
    }

    /// <summary>Hides the banner until the next start.</summary>
    [RelayCommand]
    private void Later()
    {
        IsDismissed = true;
        SetStatus(null);
    }

    /// <summary>Never offers this version again (a newer one will be offered).</summary>
    [RelayCommand]
    private void Skip()
    {
        if (Available is { } r && _settings is not null)
        {
            _settings.Save(_settings.Current with { SkippedUpdate = UpdatePlatform.Display(r.Version) });
        }

        Available = null;
        Later();
    }

    /// <summary>Menu "Support Hoshi" and the thank-you banner: opens GitHub Sponsors.</summary>
    [RelayCommand]
    private async Task Support()
    {
        UpdatedTo = null;
        if (_browser is not null)
        {
            await _browser.OpenAsync(SponsorPage, CancellationToken.None);
        }
    }

    [RelayCommand]
    private void DismissThanks() => UpdatedTo = null;

    /// <summary>Remembers this version; when it is newer than the last one that ran, says thanks once.</summary>
    private void NoteRun()
    {
        if (_settings is null)
        {
            return;
        }

        Version current = _updates.CurrentVersion;
        string now = UpdatePlatform.Display(current);
        string? last = _settings.Current.LastRunVersion;
        if (last == now)
        {
            return;
        }

        if (UpdatePlatform.ParseVersion(last) is { } previous && previous < current)
        {
            UpdatedTo = current;
        }

        _settings.Save(_settings.Current with { LastRunVersion = now });
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, token);
            while (!token.IsCancellationRequested)
            {
                await CheckAsync(token);
                await Task.Delay(CheckInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private void SetStatus(string? key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(ShowThanks));
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline))
        {
            OnPropertyChanged(nameof(IsBlocked));
        }
    }
}
