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

/// <summary>A goban, stone set or background in Preferences, with a thumbnail drawn like the board draws it.</summary>
public sealed partial class SkinOption : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private Avalonia.Media.IImage? _preview;

    public SkinOption(string? id, string name, Avalonia.Media.IImage? preview, bool wide = false)
    {
        Id = id;
        _name = name;
        _preview = preview;
        IsWide = wide;
    }

    /// <summary>The skin's id; null = "as in the theme".</summary>
    public string? Id { get; }

    /// <summary>Stone thumbnails show a white and a black stone side by side.</summary>
    public bool IsWide { get; }

    public double PreviewWidth => IsWide ? 60 : 30;
}

/// <summary>A GTP engine in Preferences → Engines (Hoshi's KataGo is listed but read-only).</summary>
public sealed partial class EngineItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _executable = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _initCommands = string.Empty;

    public bool IsBuiltIn { get; init; }

    public bool IsEditable => !IsBuiltIn;

    public Services.EngineEntry ToEntry() => new(Name.Trim(), Executable.Trim().Trim('"'), Arguments.Trim(), InitCommands);
}

/// <summary>An engine for the analysis panel: null id = Hoshi's KataGo (analysis engine).</summary>
public sealed record AnalysisEngineOption(string? Id, string Label)
{
    public override string ToString() => Label;
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
    private bool _chatSounds = true;

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

    [ObservableProperty]
    private SkinOption _selectedBoard;

    [ObservableProperty]
    private SkinOption _selectedStones;

    [ObservableProperty]
    private SkinOption _selectedBackground;

    private static readonly BoardState PreviewPosition = BoardState.Create(9).Setup(
    [
        (new Point(2, 2), Stone.Black), (new Point(6, 2), Stone.White), (new Point(2, 6), Stone.White), (new Point(6, 6), Stone.Black),
        (new Point(3, 2), Stone.White), (new Point(2, 3), Stone.Black), (new Point(5, 6), Stone.White), (new Point(6, 5), Stone.Black),
        (new Point(4, 4), Stone.Black), (new Point(5, 3), Stone.White), (new Point(3, 5), Stone.White), (new Point(5, 5), Stone.Black),
        (new Point(4, 3), Stone.White), (new Point(3, 4), Stone.Black), (new Point(7, 2), Stone.White), (new Point(2, 7), Stone.Black),
    ]);

    public PreferencesViewModel(
        ThemeService themes,
        Services.ISettingsService? settings = null,
        Services.AnalysisEngineHost? engine = null,
        Services.IFilePickerService? picker = null,
        Services.ISoundService? sounds = null,
        Services.Music.IMusicService? music = null,
        KataGoSetupViewModel? kataGoSetup = null,
        Services.Engines.IGtpEngineHost? engines = null,
        Services.Engines.AnalysisEngineSwitch? analysisSwitch = null)
    {
        _engines = engines;
        _analysisSwitch = analysisSwitch;
        KataGoSetup = kataGoSetup;
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
            _chatSounds = s.ChatSounds;
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
        BoardOptions = [new SkinOption(null, string.Empty, null), .. Skins.Boards.Select(k => new SkinOption(k.Id, k.Name, SkinThumbnails.Board(Skins.Compose(themes.Current.Board, k, null))))];
        StoneOptions = [new SkinOption(null, string.Empty, null, wide: true), .. Skins.Stones.Select(k => new SkinOption(k.Id, k.Name, SkinThumbnails.Stones(Skins.Compose(themes.Current.Board, null, k)), wide: true))];
        BackgroundOptions = [new SkinOption(null, string.Empty, null), .. Skins.Backgrounds.Select(k => new SkinOption(k.Id, k.Name, SkinThumbnails.Background(k.Kind)))];
        DescribeTheme();
        _selectedBoard = BoardOptions.First(o => o.Id == themes.Board?.Id);
        _selectedStones = StoneOptions.First(o => o.Id == themes.Stones?.Id);
        _selectedBackground = BackgroundOptions.First(o => o.Id == themes.Background?.Id);
        _themes.Changed += (_, _) =>
        {
            DescribeTheme();
            OnPropertyChanged(nameof(PreviewStyle));
            OnPropertyChanged(nameof(PreviewBackground));
        };
        LoadEngines();
        _loaded = true;
    }

    // ---------- Engines (GTP) ----------

    private readonly Services.Engines.IGtpEngineHost? _engines;
    private readonly Services.Engines.AnalysisEngineSwitch? _analysisSwitch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditableEngine), nameof(SelectedIsBuiltIn))]
    [NotifyCanExecuteChangedFor(nameof(RemoveEngineCommand), nameof(TestGtpEngineCommand))]
    private EngineItem? _selectedEngineItem;

    [ObservableProperty]
    private AnalysisEngineOption? _selectedAnalysisEngine;

    [ObservableProperty]
    private string? _enginesStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestGtpEngineCommand))]
    private bool _isTestingGtp;

    public bool HasEngineHost => _engines is not null;

    public System.Collections.ObjectModel.ObservableCollection<EngineItem> EngineItems { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<AnalysisEngineOption> AnalysisEngineOptions { get; } = [];

    public bool HasEditableEngine => SelectedEngineItem is { IsBuiltIn: false };

    public bool SelectedIsBuiltIn => SelectedEngineItem is { IsBuiltIn: true };

    public string BuiltInEngineHint => Tr.F("Prefs.EngineBuiltIn", Services.KataGo.KataGoLocator.GtpConfigName);

    private void LoadEngines()
    {
        EngineItems.Clear();
        if (_engines?.Engines.FirstOrDefault(e => e.IsBuiltIn) is { } builtIn)
        {
            EngineItems.Add(new EngineItem
            {
                IsBuiltIn = true,
                Name = builtIn.Name,
                Executable = builtIn.Config.Executable,
                Arguments = builtIn.Config.Arguments,
            });
        }

        foreach (Services.EngineEntry e in _settings?.Current.Engines ?? [])
        {
            EngineItems.Add(new EngineItem { Name = e.Name, Executable = e.Executable, Arguments = e.Arguments, InitCommands = e.InitCommands });
        }

        SelectedEngineItem = EngineItems.FirstOrDefault(i => !i.IsBuiltIn) ?? EngineItems.FirstOrDefault();
        RefreshAnalysisOptions();
    }

    private void RefreshAnalysisOptions()
    {
        string? current = SelectedAnalysisEngine?.Id ?? _settings?.Current.AnalysisEngine;
        AnalysisEngineOptions.Clear();
        AnalysisEngineOptions.Add(new AnalysisEngineOption(null, Tr.T("Prefs.AnalysisBuiltIn")));
        foreach (EngineItem item in EngineItems.Where(i => !i.IsBuiltIn && i.Name.Trim().Length > 0))
        {
            AnalysisEngineOptions.Add(new AnalysisEngineOption(item.Name.Trim(), item.Name.Trim()));
        }

        SelectedAnalysisEngine = AnalysisEngineOptions.FirstOrDefault(o => o.Id == current) ?? AnalysisEngineOptions[0];
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void AddEngine()
    {
        string name = Tr.T("Prefs.EngineNew");
        for (int n = 2; EngineItems.Any(i => i.Name == name); n++)
        {
            name = $"{Tr.T("Prefs.EngineNew")} {n}";
        }

        var item = new EngineItem { Name = name };
        EngineItems.Add(item);
        SelectedEngineItem = item;
        EnginesStatus = null;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(HasEditableEngine))]
    private void RemoveEngine()
    {
        if (SelectedEngineItem is { IsBuiltIn: false } item)
        {
            EngineItems.Remove(item);
            SelectedEngineItem = EngineItems.LastOrDefault();
            SaveEngines();
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseEngineExecutable()
    {
        if (SelectedEngineItem is { IsBuiltIn: false } item && await Pick(Tr.T("Prefs.PickExecutable")) is { } path)
        {
            item.Executable = path;
        }
    }

    /// <summary>Saves the engine list and the analysis choice; false when a name is missing or repeated.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveEngines() => TrySaveEngines();

    private bool TrySaveEngines()
    {
        if (_settings is null)
        {
            return false;
        }

        Services.EngineEntry[] entries = [.. EngineItems.Where(i => !i.IsBuiltIn).Select(i => i.ToEntry())];
        var reserved = new HashSet<string>(EngineItems.Where(i => i.IsBuiltIn).Select(i => i.Name), StringComparer.Ordinal);
        if (entries.Any(e => e.Name.Length == 0) || entries.GroupBy(e => e.Name).Any(g => g.Count() > 1) || entries.Any(e => reserved.Contains(e.Name)))
        {
            EnginesStatus = Tr.T("Prefs.EngineNameTaken");
            return false;
        }

        RefreshAnalysisOptions();
        Services.AppSettings updated = _settings.Current with
        {
            Engines = entries,
            AnalysisEngine = SelectedAnalysisEngine?.Id,
        };
        _settings.Save(updated);
        _engines?.Refresh(updated);
        _analysisSwitch?.Reconfigure();
        EnginesStatus = Tr.T("Prefs.EngineListSaved");
        return true;
    }

    partial void OnSelectedAnalysisEngineChanged(AnalysisEngineOption? value)
    {
        if (_loaded && value is not null && _settings is not null && _settings.Current.AnalysisEngine != value.Id)
        {
            _settings.Save(_settings.Current with { AnalysisEngine = value.Id });
            _analysisSwitch?.Reconfigure();
        }
    }

    /// <summary>Saves, starts the selected engine and reports its name, commands and analysis support.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanTestGtp))]
    private async Task TestGtpEngine()
    {
        if (_engines is null || SelectedEngineItem is not { } item || !TrySaveEngines())
        {
            return;
        }

        string id = item.IsBuiltIn ? Services.Engines.GtpEngineHost.BuiltInId : item.Name.Trim();
        if (_engines.Find(id) is not { } choice)
        {
            return;
        }

        IsTestingGtp = true;
        EnginesStatus = Tr.T("Prefs.EngineTesting");
        try
        {
            await _engines.StopAsync(id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            Hoshi.Engines.Gtp.GtpEngine engine = await _engines.AcquireAsync(choice, timeout.Token);
            string analysis = engine.AnalysisKind switch
            {
                Hoshi.Engines.Gtp.GtpAnalysisKind.KataGo => Tr.F("Prefs.EngineTestAnalysis", "kata-analyze"),
                Hoshi.Engines.Gtp.GtpAnalysisKind.Leela => Tr.F("Prefs.EngineTestAnalysis", "lz-analyze"),
                _ => Tr.T("Prefs.EngineTestNoAnalysis"),
            };
            EnginesStatus = Tr.F("Prefs.EngineTestOk", engine.DisplayName, engine.Commands.Count, analysis);
        }
        catch (Exception ex) when (ex is Hoshi.Engines.KataGo.EngineException or OperationCanceledException)
        {
            EnginesStatus = ex is OperationCanceledException ? Tr.T("Prefs.EngineTimeout") : ex.Message;
        }
        finally
        {
            IsTestingGtp = false;
        }
    }

    private bool CanTestGtp() => !IsTestingGtp && SelectedEngineItem is not null && _engines is not null;

    public IReadOnlyList<SkinOption> BoardOptions { get; }

    public IReadOnlyList<SkinOption> StoneOptions { get; }

    public IReadOnlyList<SkinOption> BackgroundOptions { get; }

    /// <summary>The sample position of the "Board &amp; stones" preview.</summary>
    public BoardState PreviewBoard => PreviewPosition;

    public BoardStyle PreviewStyle => _themes.EffectiveBoard;

    public BackgroundKind PreviewBackground => _themes.EffectiveBackground;

    partial void OnSelectedBoardChanged(SkinOption value) => ApplySkins();

    partial void OnSelectedStonesChanged(SkinOption value) => ApplySkins();

    partial void OnSelectedBackgroundChanged(SkinOption value) => ApplySkins();

    /// <summary>Back to the theme's own goban, stones and background.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ResetSkins()
    {
        SelectedBoard = BoardOptions[0];
        SelectedStones = StoneOptions[0];
        SelectedBackground = BackgroundOptions[0];
    }

    private void ApplySkins()
    {
        if (_loaded)
        {
            _themes.SelectSkins(Skins.Board(SelectedBoard?.Id), Skins.StoneSet(SelectedStones?.Id), Skins.Background(SelectedBackground?.Id));
        }
    }

    /// <summary>The "as in the theme" entries say what the current theme uses, with its picture.</summary>
    private void DescribeTheme()
    {
        BoardStyle theme = _themes.Current.Board;
        BoardOptions[0].Name = Tr.F("Skin.FromThemeNamed", Skins.BoardNameFor(theme));
        BoardOptions[0].Preview = SkinThumbnails.Board(theme);
        StoneOptions[0].Name = Tr.F("Skin.FromThemeNamed", Skins.StonesNameFor(theme));
        StoneOptions[0].Preview = SkinThumbnails.Stones(theme);
        BackgroundKind background = _themes.Current.Background;
        BackgroundOptions[0].Name = Tr.F("Skin.FromThemeNamed", Skins.Background(background)?.Name ?? string.Empty);
        BackgroundOptions[0].Preview = SkinThumbnails.Background(background);
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

    /// <summary>One-click KataGo install (null without a host).</summary>
    public KataGoSetupViewModel? KataGoSetup { get; }

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

    partial void OnChatSoundsChanged(bool value) => SaveEffects();

    partial void OnAtariAlertsChanged(bool value) => SaveEffects();

    /// <summary>Why the music cannot play on this system, if so.</summary>
    public string? MusicProblem => _musicService?.Problem;

    partial void OnMusicChanged(bool value)
    {
        if (value && _settings?.Current.Muted != true)
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
                ChatSounds = ChatSounds,
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
