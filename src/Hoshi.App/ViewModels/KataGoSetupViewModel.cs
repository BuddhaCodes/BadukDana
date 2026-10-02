using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.KataGo;
using Hoshi.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>Installs KataGo with one click when Hoshi has none (shown on the board and in Preferences → Analysis).</summary>
public interface IKataGoInstaller
{
    bool IsSupported { get; }

    int DownloadMegabytes { get; }

    Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken);
}

public sealed class DefaultKataGoInstaller(IHttpClientFactory http, ILogger<KataGoInstaller>? logger = null) : IKataGoInstaller
{
    public bool IsSupported => KataGoInstaller.IsSupported;

    public int DownloadMegabytes => KataGoInstaller.DownloadMegabytes;

    public Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken) =>
        new KataGoInstaller(http.CreateClient(AppHost.UpdatesHttpClient), AppPaths.DataDirectory, logger).InstallAsync(progress, cancellationToken);
}

public sealed partial class KataGoSetupViewModel : ViewModelBase
{
    private readonly IKataGoInstaller _installer;
    private readonly AnalysisEngineHost? _engine;
    private readonly ISettingsService? _settings;
    private readonly ILogger _logger;
    private string? _statusKey;
    private object?[] _statusArgs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptVisible))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    public KataGoSetupViewModel(IKataGoInstaller installer, AnalysisEngineHost? engine = null, ISettingsService? settings = null, ILogger<KataGoSetupViewModel>? logger = null)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _engine = engine;
        _settings = settings;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        if (_engine is not null)
        {
            _engine.Changed += (_, _) => Refresh();
        }
    }

    /// <summary>No KataGo anywhere (not configured, not bundled, not installed).</summary>
    public bool IsNeeded => _engine is { Source: null };

    /// <summary>The card over the board: shown until installed or dismissed ("Not now" is remembered).</summary>
    public bool IsPromptVisible => (IsNeeded || IsInstalling) && !(_settings?.Current.KataGoPromptDismissed ?? false);

    public bool CanInstallHere => _installer.IsSupported;

    public string InstallLabel => Tr.F("KataGo.Install", _installer.DownloadMegabytes);

    public string Explanation => CanInstallHere ? Tr.T("KataGo.Explanation") : Tr.T("KataGo.NoBuild");

    public string? StatusText => _statusKey is null ? null : Tr.F(_statusKey, _statusArgs);

    public bool HasStatus => StatusText is not null;

    private bool CanInstall() => !IsInstalling && CanInstallHere;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        IsInstalling = true;
        Progress = 0;
        SetStatus("KataGo.Downloading", 0);
        try
        {
            var progress = new Progress<double>(p =>
            {
                // Progress<T> posts its reports: one can arrive after the install finished; it must not undo the result.
                if (!IsInstalling)
                {
                    return;
                }

                Progress = p;
                SetStatus("KataGo.Downloading", (int)Math.Round(p * 100));
            });
            await _installer.InstallAsync(progress, CancellationToken.None);
            if (_engine is not null && _settings is not null)
            {
                _engine.Reconfigure(_settings.Current);
            }

            SetStatus(IsNeeded ? "KataGo.StillMissing" : "KataGo.Ready");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or PlatformNotSupportedException or JsonException)
        {
            _logger.LogWarning(ex, "KataGo install failed");
            SetStatus("KataGo.Failed", ex.Message);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void NotNow()
    {
        if (_settings is not null)
        {
            _settings.Save(_settings.Current with { KataGoPromptDismissed = true });
        }

        Refresh();
    }

    private void SetStatus(string? key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsNeeded));
        OnPropertyChanged(nameof(IsPromptVisible));
    }
}
