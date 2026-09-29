using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hoshi.App.Services;

namespace Hoshi.App.Themes;

/// <summary>
/// Applies a <see cref="HoshiTheme"/> by replacing application resources that the views read with
/// <c>DynamicResource</c>: colour brushes, fonts, the icon set (<c>Icon.*</c>), the board style
/// (<c>Theme.Board</c>), the background (<c>Theme.Background</c>) and whether to animate (<c>Theme.Animations</c>).
/// Switching is live: nothing needs to be rebuilt.
/// </summary>
public sealed class ThemeService
{
    private readonly ISettingsService? _settings;

    public ThemeService(ISettingsService? settings = null)
    {
        _settings = settings;
        Current = HoshiThemes.ById(settings?.Current.Theme);
        Animations = settings?.Current.Animations ?? true;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HoshiTheme> Themes => HoshiThemes.All;

    public HoshiTheme Current { get; private set; }

    public bool Animations { get; private set; }

    /// <summary>Applies the saved theme (at start-up).</summary>
    public void ApplyCurrent(IResourceDictionary? resources = null) => Apply(Current, Animations, resources, save: false);

    public void Select(HoshiTheme theme, bool animations) => Apply(theme, animations, null, save: true);

    public static void Apply(HoshiTheme theme, bool animations, IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(resources);
        void Brush(string key, Color c)
        {
            resources[key + ".Color"] = c;
            resources[key] = new SolidColorBrush(c);
        }

        Brush("Bg.Window", theme.Window);
        Brush("Bg.Panel", theme.Properties);
        Brush("Bg.Bar", theme.Bar);
        Brush("Bg.Sidebar", theme.Sidebar);
        Brush("Bg.Properties", theme.Properties);
        Brush("Bg.PanelAlt", theme.PanelAlt);
        Brush("Border.Subtle", theme.Border);
        Brush("Text.Primary", theme.Text);
        Brush("Text.Secondary", theme.TextSecondary);
        Brush("Accent", theme.Accent);
        Brush("Danger", theme.Danger);
        Brush("Bg.EditBar", theme.EditBar);
        Brush("EditBar.Foreground", theme.EditBarText);
        Brush("EditBar.Hover", theme.EditBarHover);
        Brush("Bg.Surround", theme.Surround);

        // Fluent's own accent follows the theme, so selections, focus rings and accent buttons match.
        resources["SystemAccentColor"] = theme.Accent;
        resources["SystemAccentColorDark1"] = Shade(theme.Accent, 0.85);
        resources["SystemAccentColorLight1"] = Shade(theme.Accent, 1.1);

        resources["Font.UI"] = new FontFamily(theme.UiFont);
        resources["Font.Title"] = new FontFamily(theme.TitleFont);
        resources["Font.Mono"] = new FontFamily(theme.MonoFont);
        resources["Font.Size.Title"] = theme.TitleSize;

        IReadOnlyDictionary<string, IconData> icons = IconSets.All[theme.IconSet];
        foreach ((string key, IconData icon) in icons)
        {
            resources["Icon." + key] = icon;
        }

        resources["Theme.Board"] = theme.Board;
        resources["Theme.Background"] = theme.Background;
        resources["Theme.Animations"] = animations;
        resources["Theme.Id"] = theme.Id;
    }

    private void Apply(HoshiTheme theme, bool animations, IResourceDictionary? resources, bool save)
    {
        Current = theme;
        Animations = animations;
        if ((resources ?? Application.Current?.Resources) is { } target)
        {
            Apply(theme, animations, target);
        }

        if (save && _settings is not null)
        {
            _settings.Save(_settings.Current with { Theme = theme.Id, Animations = animations });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Color Shade(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));
}
