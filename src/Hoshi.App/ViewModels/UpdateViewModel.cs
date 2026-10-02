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
/// The update banner. Shortly after start (and every 12 hours) asks GitHub for the latest release; when it is
/// newer, offers it. "Update" downloads this system's file, verifies its checksum, puts it in place and restarts
/// Hoshi; a copy that cannot replace itself (run from source, or installed somewhere read-only) opens the download
/// page instead. Never during the player's live OGS game.
/// </summary>
public sealed partial class UpdateViewModel : ViewModelBase
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

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
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(Text))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private ReleaseInfo? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isDismissed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(CheckNowCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

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
    }

    /// <summary>The banner shows a newer release (or the result of a manual check) until dismissed.</summary>
    public bool IsVisible => !IsDismissed && (Available is not null || _statusKey is not null);

    public string CurrentVersionText => UpdatePlatform.Display(_updates.CurrentVersion);

    public string? Text => Available is { } r
        ? Tr.F("Update.Available", UpdatePlatform.Display(r.Version), CurrentVersionText)
        : null;

    public string? StatusText => _statusKey is null ? null : Tr.F(_statusKey, _statusArgs);

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
                await _browser.OpenAsync(release.PageUrl, CancellationToken.None);
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
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                SetStatus("Update.Downloading", (int)Math.Round(p * 100));
            });
            string archive = await _updates.DownloadAsync(release, progress, CancellationToken.None);
            SetStatus("Update.Installing");
            string installed = await Task.Run(() => _updates.Install(archive));
            SetStatus("Update.Restarting");
            _updates.Launch(installed);
            _shutdown?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Update failed");
            SetStatus("Update.Failed", ex.Message);
        }
        finally
        {
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
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline))
        {
            OnPropertyChanged(nameof(IsBlocked));
        }
    }
}
