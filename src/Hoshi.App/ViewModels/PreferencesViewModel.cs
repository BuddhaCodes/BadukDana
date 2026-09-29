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

/// <summary>Preferences: theme and animations. Changes apply immediately and are saved.</summary>
public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly ThemeService _themes;

    [ObservableProperty]
    private ThemeCard _selected;

    [ObservableProperty]
    private bool _animations;

    public PreferencesViewModel(ThemeService themes)
    {
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
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
}
