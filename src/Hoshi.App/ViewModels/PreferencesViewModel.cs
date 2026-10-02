using CommunityToolkit.Mvvm.ComponentModel;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>A theme as shown in Preferences, with a small sample position drawn in its own style.</summary>
public sealed partial class ThemeCard : ObservableObject
{
    private static readonly BoardState Sample = BoardState.Create(7)
        .Setup([(new Point(2, 2), Stone.Black), (new Point(4, 4), Stone.White), (new Point(4, 2), Stone.Black), (new Point(2, 4), Stone.White), (new Point(3, 3), Stone.Black)]);

    [ObservableProperty]
    private bool _isSelected;

    public ThemeCard(HoshiTheme theme) => Theme = theme;

    public HoshiTheme Theme { get; }

    public string Name => Theme.Name;

    public string Description => Theme.Description;

    public BoardState SampleBoard => Sample;

    public Avalonia.Media.IBrush Accent => new Avalonia.Media.SolidColorBrush(Theme.Accent);

    public Avalonia.Media.IBrush Chrome => new Avalonia.Media.SolidColorBrush(Theme.Bar);

    public Avalonia.Media.IBrush Surround => new Avalonia.Media.SolidColorBrush(Theme.Surround);

    public Avalonia.Media.FontFamily TitleFont => new(Theme.TitleFont);
}

/// <summary>A user interface language choice ("en", "es") with its own name.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>Preferences: theme and animations, and the KataGo analysis engine. Changes apply immediately and are saved.</summary>
public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly ThemeService _themes;
    private readonly Services.ISettingsService? _settings;
    private readonly Services.AnalysisEngineHost? _engine;
    private readonly Services.IFilePickerService? _picker;
    private readonly Services.ISoundService? _sounds;
    private readonly bool _loaded;

    [ObservableProperty]
    private bool _moveEffects = true;

    [ObservableProperty]
    private int _soundVolume = 70;

    [ObservableProperty]
    private bool _stoneSounds = true;

    [ObservableProperty]
    private bool _atariAlerts = true;

    [ObservableProperty]
    private bool _music = true;

    [ObservableProperty]
    private int _musicVolume = 35;

    private readonly Services.Music.IMusicService? _musicService;

    [ObservableProperty]
    private string _kataGoExecutable = string.Empty;

    [ObservableProperty]
    private string _kataGoModel = string.Empty;

    [ObservableProperty]
    private string _kataGoConfig = string.Empty;

    [ObservableProperty]
    private int _analysisVisits = 200;

    [ObservableProperty]
    private string? _engineStatus;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private ThemeCard _selected;

    [ObservableProperty]
    private bool _animations;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public PreferencesViewModel(
        ThemeService themes,
        Services.ISettingsService? settings = null,
        Services.AnalysisEngineHost? engine = null,
        Services.IFilePickerService? picker = null,
        Services.ISoundService? sounds = null,
        Services.Music.IMusicService? music = null)
    {
        _sounds = sounds;
        _musicService = music;
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
        _settings = settings;
        _engine = engine;
        _picker = picker;
        if (settings?.Current is { } s)
        {
            _kataGoExecutable = s.KataGoExecutable ?? string.Empty;
            _kataGoModel = s.KataGoModel ?? string.Empty;
            _kataGoConfig = s.KataGoConfig ?? string.Empty;
            _analysisVisits = s.AnalysisVisits;
            _moveEffects = s.MoveEffects;
            _soundVolume = s.SoundVolume;
            _music = s.Music;
            _stoneSounds = s.StoneSounds;
            _atariAlerts = s.AtariAlerts;
            _musicVolume = s.MusicVolume;
            _checkForUpdates = s.CheckForUpdates;
        }

        string language = Tr.Normalize(settings?.Current.Language ?? Tr.English);
        _selectedLanguage = LanguageOptions.First(l => l.Code == language);
        _engineStatus = engine?.Problem ?? (engine is null ? null : Tr.T("Prefs.EngineConfigured"));
        Cards = [.. themes.Themes.Select(t => new ThemeCard(t))];
        _selected = Cards.First(c => c.Theme.Id == themes.Current.Id);
        _selected.IsSelected = true;
        _animations = themes.Animations;
        _loaded = true;
    }

    /// <summary>Ask GitHub for a newer Hoshi (saved at once).</summary>
    [ObservableProperty]
    private bool _checkForUpdates = true;

    public string UpdatesHint => Tr.F("Prefs.UpdatesHint", Services.Updates.UpdatePlatform.Display(Services.Updates.UpdatePlatform.CurrentVersion));

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_loaded && _settings is not null)
        {
            _settings.Save(_settings.Current with { CheckForUpdates = value });
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = [new("en", "English"), new("es", "Español")];

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        Tr.SetLanguage(value.Code);
        if (_loaded && _settings is not null)
        {
            _settings.Save(_settings.Current with { Language = value.Code });
        }
    }

    /// <summary>Folder where the user can put their own impact_small / explosion_medium / explosion_big (.wav or .mp3).</summary>
    public string? CustomSoundFolder => (_sounds as Services.SystemSoundService)?.CustomDirectory;

    /// <summary>What each celebration sounds like, and where custom sounds go.</summary>
    public string SoundsHelp => Tr.F("Prefs.SoundsHelp", CustomSoundFolder ?? "sounds");

    partial void OnMoveEffectsChanged(bool value) => SaveEffects();

    partial void OnSoundVolumeChanged(int value) => SaveEffects();

    partial void OnStoneSoundsChanged(bool value) => SaveEffects();

    partial void OnAtariAlertsChanged(bool value) => SaveEffects();

    /// <summary>Why the music cannot play on this system, if so.</summary>
    public string? MusicProblem => _musicService?.Problem;

    partial void OnMusicChanged(bool value)
    {
        if (value)
        {
            _musicService?.Start();
        }
        else
        {
            _musicService?.Stop();
        }

        SaveEffects();
        OnPropertyChanged(nameof(MusicProblem));
    }

    partial void OnMusicVolumeChanged(int value)
    {
        _musicService?.SetVolume(Math.Clamp(value, 0, 100) / 100.0);
        SaveEffects();
    }

    private void SaveEffects()
    {
        if (_loaded && _settings is not null)
        {
            _settings.Save(_settings.Current with
            {
                MoveEffects = MoveEffects,
                SoundVolume = Math.Clamp(SoundVolume, 0, 100),
                Music = Music,
                StoneSounds = StoneSounds,
                AtariAlerts = AtariAlerts,
                MusicVolume = Math.Clamp(MusicVolume, 0, 100),
            });
        }
    }

    /// <summary>Plays the best-move explosion at the chosen volume.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void TestSound() => _sounds?.Play(Services.SoundEffect.ExplosionBig, Math.Clamp(SoundVolume, 0, 100) / 100.0);

    public IReadOnlyList<ThemeCard> Cards { get; }

    partial void OnSelectedChanged(ThemeCard? oldValue, ThemeCard newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
        _themes.Select(newValue.Theme, Animations);
    }

    partial void OnAnimationsChanged(bool value) => _themes.Select(Selected.Theme, value);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseExecutable() => KataGoExecutable = await Pick(Tr.T("Prefs.PickExecutable")) ?? KataGoExecutable;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseModel() => KataGoModel = await Pick(Tr.T("Prefs.PickModel")) ?? KataGoModel;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseConfig() => KataGoConfig = await Pick(Tr.T("Prefs.PickConfig")) ?? KataGoConfig;

    /// <summary>Saves the engine settings and restarts KataGo with them.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ApplyEngine()
    {
        if (_settings is null)
        {
            return;
        }

        Services.AppSettings updated = _settings.Current with
        {
            KataGoExecutable = Clean(KataGoExecutable),
            KataGoModel = Clean(KataGoModel),
            KataGoConfig = Clean(KataGoConfig),
            AnalysisVisits = Math.Clamp(AnalysisVisits, 10, 100_000),
        };
        _settings.Save(updated);
        _engine?.Reconfigure(updated);
        EngineStatus = _engine?.Problem ?? Tr.T("Prefs.EngineSaved");
    }

    /// <summary>Saves, then asks KataGo for a quick analysis of an empty 9×9 board.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task TestEngine()
    {
        ApplyEngine();
        if (_engine is null || _engine.Problem is not null)
        {
            return;
        }

        IsTesting = true;
        EngineStatus = Tr.T("Prefs.TestingEngine");
        void ShowActivity(object? sender, EventArgs e) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => EngineStatus = _engine.Activity ?? Tr.T("Prefs.TestingEngine"));
        _engine.ActivityChanged += ShowActivity;
        try
        {
            // The first OpenCL run tunes the GPU, which can take many minutes on integrated graphics.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            IReadOnlyList<Hoshi.Engines.KataGo.TurnAnalysis> r = await _engine.AnalyzeAsync(
                new Hoshi.Engines.KataGo.AnalysisQuery { Width = 9, Height = 9, Komi = 7, MaxVisits = 16, IncludeOwnership = false },
                timeout.Token);
            string best = r[0].Best?.Point is { } p ? p.ToHuman(9) : Tr.T("Game.PassNoun");
            EngineStatus = Tr.F("Prefs.EngineWorks", best);
        }
        catch (Exception ex) when (ex is Hoshi.Engines.KataGo.EngineException or OperationCanceledException)
        {
            EngineStatus = ex is OperationCanceledException ? Tr.T("Prefs.EngineTimeout") : ex.Message;
        }
        finally
        {
            _engine.ActivityChanged -= ShowActivity;
            IsTesting = false;
        }
    }

    private async Task<string?> Pick(string title) => _picker is null ? null : await _picker.PickFileAsync(title);

    private static string? Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('"');
}
