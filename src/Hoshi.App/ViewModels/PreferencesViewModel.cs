using CommunityToolkit.Mvvm.ComponentModel;
using Hoshi.App.Themes;
using Hoshi.Core;

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

/// <summary>Preferences: theme and animations, and the KataGo analysis engine. Changes apply immediately and are saved.</summary>
public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly ThemeService _themes;
    private readonly Services.ISettingsService? _settings;
    private readonly Services.AnalysisEngineHost? _engine;
    private readonly Services.IFilePickerService? _picker;

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

    public PreferencesViewModel(
        ThemeService themes,
        Services.ISettingsService? settings = null,
        Services.AnalysisEngineHost? engine = null,
        Services.IFilePickerService? picker = null)
    {
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
        }

        _engineStatus = engine?.Problem ?? (engine is null ? null : "KataGo configurado.");
        Cards = [.. themes.Themes.Select(t => new ThemeCard(t))];
        _selected = Cards.First(c => c.Theme.Id == themes.Current.Id);
        _selected.IsSelected = true;
        _animations = themes.Animations;
    }

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
    private async Task BrowseExecutable() => KataGoExecutable = await Pick("Ejecutable de KataGo (katago / katago.exe)") ?? KataGoExecutable;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseModel() => KataGoModel = await Pick("Red neuronal de KataGo (.bin.gz)") ?? KataGoModel;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task BrowseConfig() => KataGoConfig = await Pick("Configuración de análisis (analysis_example.cfg)") ?? KataGoConfig;

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
        EngineStatus = _engine?.Problem ?? "Guardado. KataGo arrancará con el primer análisis.";
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
        EngineStatus = "Probando KataGo…";
        void ShowActivity(object? sender, EventArgs e) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => EngineStatus = _engine.Activity ?? "Probando KataGo…");
        _engine.ActivityChanged += ShowActivity;
        try
        {
            // The first OpenCL run tunes the GPU, which can take many minutes on integrated graphics.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            IReadOnlyList<Hoshi.Engines.KataGo.TurnAnalysis> r = await _engine.AnalyzeAsync(
                new Hoshi.Engines.KataGo.AnalysisQuery { Width = 9, Height = 9, Komi = 7, MaxVisits = 16, IncludeOwnership = false },
                timeout.Token);
            string best = r[0].Best?.Point is { } p ? p.ToHuman(9) : "pase";
            EngineStatus = $"KataGo funciona: en 9×9 vacío propone {best}.";
        }
        catch (Exception ex) when (ex is Hoshi.Engines.KataGo.EngineException or OperationCanceledException)
        {
            EngineStatus = ex is OperationCanceledException ? "KataGo no respondió a tiempo." : ex.Message;
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
