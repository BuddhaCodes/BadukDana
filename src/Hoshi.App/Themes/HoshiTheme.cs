using Avalonia.Media;
using Hoshi.Core.Localization;

namespace Hoshi.App.Themes;

/// <summary>What is drawn around the board.</summary>
public enum BackgroundKind
{
    /// <summary>Night sky: slow twinkling stars, drifting nebula, faint constellations, a rare shooting star.</summary>
    NightSky,

    /// <summary>Ink wash: soft sumi-e clouds that drift and dissolve over dark paper.</summary>
    InkMist,

    /// <summary>Raked sand of a zen garden, with ripples around two stones, moving very slowly.</summary>
    ZenSand,

    /// <summary>Washi paper with fibres (static).</summary>
    Washi,

    /// <summary>Indigo cotton with white seigaiha stitching (Hoshi's art, tiled).</summary>
    Sashiko,

    /// <summary>A dark walnut table top (tiled).</summary>
    WalnutTable,

    /// <summary>Dark layered slate (tiled).</summary>
    Slate,

    /// <summary>Charcoal linen (tiled).</summary>
    Linen,

    /// <summary>A bamboo blind (tiled).</summary>
    Sudare,

    /// <summary>Mochi: warm cream with soft pastel clouds (pink, mint, lavender, butter) drifting very slowly.</summary>
    Mochi,

    /// <summary>Matcha: pale tea green with mint and cream clouds.</summary>
    Matcha,

    /// <summary>Yuzu: warm butter with peach and citrus clouds.</summary>
    Yuzu,

    /// <summary>Kuro mochi: a deep plum night with dim lavender, dusk-pink and teal clouds.</summary>
    MochiNight,
}

/// <summary>The little effect played when a stone is placed.</summary>
public enum PlacementEffect
{
    None,

    /// <summary>A golden halo expanding from the stone.</summary>
    GoldHalo,

    /// <summary>A dark ink ring that spreads and fades.</summary>
    InkRipple,

    /// <summary>A pale ripple, like a pebble dropped in raked sand.</summary>
    SandRipple,

    /// <summary>Only the stone settling, no ring.</summary>
    Settle,
}

/// <summary>How stones are drawn.</summary>
public enum StoneStyle
{
    /// <summary>Vector stones with a soft vertical gradient (the colours of Shudan's SVG stones).</summary>
    Gradient,

    /// <summary>Glassy slate with a cool highlight and pearly white stones.</summary>
    Pearl,

    /// <summary>Matte slate and clam-shell white with fine growth lines.</summary>
    SlateShell,

    /// <summary>Soft, low-contrast stones with no rim.</summary>
    Soft,
}

/// <summary>How a theme draws the goban.</summary>
public sealed record BoardStyle
{
    public Color Wood { get; init; } = Color.Parse("#E3C28A");

    /// <summary>An image of the wood (<c>avares://</c>); null = Hoshi's procedural kaya tinted with <see cref="Wood"/>.</summary>
    public string? Texture { get; init; }

    /// <summary>Hoshi's rendered stone sprites (<c>Assets/Art/Stones/&lt;set&gt;</c>); null = the vector <see cref="Stones"/>.</summary>
    public string? StoneSet { get; init; }

    public int WhiteVariants { get; init; }

    public int BlackVariants { get; init; }

    public Color Border { get; init; } = Color.Parse("#CA933A");

    /// <summary>Border width in cells (0 = none).</summary>
    public double BorderWidth { get; init; } = 0.15;

    public Color Lines { get; init; } = Color.Parse("#5E2E0C");

    public Color Coordinates { get; init; } = Color.Parse("#D05E2E0C");

    public Color BoardShadow { get; init; } = Color.Parse("#CC14000F");

    public double BoardShadowBlur { get; init; } = 20;

    public Color StoneShadow { get; init; } = Color.Parse("#66170A02");

    public StoneStyle Stones { get; init; } = StoneStyle.Gradient;

    public PlacementEffect Effect { get; init; } = PlacementEffect.None;

    public Color EffectColor { get; init; } = Colors.Transparent;

    /// <summary>Colour of the last-move ring; transparent = the opposite of the stone's colour.</summary>
    public Color LastMove { get; init; } = Colors.Transparent;
}

/// <summary>A complete look for Hoshi: chrome palette, fonts, icons, background and board.</summary>
public sealed record HoshiTheme
{
    public required string Id { get; init; }

    /// <summary>Localization key of the theme's name (<see cref="Tr"/>).</summary>
    public required string NameKey { get; init; }

    /// <summary>Localization key of the theme's one-line description.</summary>
    public required string DescriptionKey { get; init; }

    public string Name => Tr.T(NameKey);

    public string Description => Tr.T(DescriptionKey);

    // Chrome
    public required Color Window { get; init; }

    public required Color Bar { get; init; }

    public required Color Sidebar { get; init; }

    public required Color Properties { get; init; }

    public required Color PanelAlt { get; init; }

    public required Color Border { get; init; }

    public required Color Text { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color Accent { get; init; }

    public Color Danger { get; init; } = Color.Parse("#D9534F");

    /// <summary>Good news: a strong move, a right answer.</summary>
    public Color Good { get; init; } = Color.Parse("#6CC47A");

    /// <summary>A light theme: Fluent's own controls switch to their light look too.</summary>
    public bool IsLight { get; init; }

    /// <summary>Soft, round corners on buttons, cards and menus (Mochi).</summary>
    public bool Rounded { get; init; }

    public required Color EditBar { get; init; }

    public required Color EditBarText { get; init; }

    public required Color EditBarHover { get; init; }

    /// <summary>Solid colour under the background (and the whole board area when animations are off).</summary>
    public required Color Surround { get; init; }

    // Type and icons
    public required string UiFont { get; init; }

    public required string TitleFont { get; init; }

    public required string MonoFont { get; init; }

    /// <summary>Title font size for player names (display serifs need a little more).</summary>
    public double TitleSize { get; init; } = 14;

    public required string IconSet { get; init; }

    public required BackgroundKind Background { get; init; }

    public required BoardStyle Board { get; init; }
}

/// <summary>The built-in themes. Fonts: OFL; icons: Phosphor/Tabler (MIT), Lucide (ISC); see THIRD_PARTY_NOTICES.md.</summary>
public static class HoshiThemes
{
    private const string Fonts = "avares://Hoshi.App/Assets/Fonts/";

    /// <summary>Estrella polar (the 2026-10 redesign): night blue, gold and a seal red, Shippori Mincho titles.</summary>
    public static HoshiTheme NightSky { get; } = new()
    {
        Id = "night",
        NameKey = "Theme.Night.Name",
        DescriptionKey = "Theme.Night.Description",
        Window = Color.Parse("#0B1026"),
        Bar = Color.Parse("#0F1530"),
        Sidebar = Color.Parse("#0F1530"),
        Properties = Color.Parse("#141A3A"),
        PanelAlt = Color.Parse("#1B2350"),
        Border = Color.Parse("#2A315A"),
        Text = Color.Parse("#E9E6DA"),
        TextSecondary = Color.Parse("#9AA0C3"),
        Accent = Color.Parse("#E8C36A"),
        Danger = Color.Parse("#E0604A"),
        Good = Color.Parse("#7FD1A5"),
        EditBar = Color.Parse("#2E3F73"),
        EditBarText = Color.Parse("#F1EEDF"),
        EditBarHover = Color.Parse("#3B4E8A"),
        Surround = Color.Parse("#0B1026"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "ShipporiMincho#Shippori Mincho",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "PhosphorLight",
        Background = BackgroundKind.NightSky,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/kaya-masame.jpg",
            StoneSet = "glass",
            WhiteVariants = 3,
            BlackVariants = 3,
            Wood = Color.Parse("#E8C483"),
            Border = Color.Parse("#C9A45C"),
            BorderWidth = 0.05,
            Lines = Color.Parse("#3B2A14"),
            Coordinates = Color.Parse("#B03B2A14"),
            BoardShadow = Color.Parse("#E6000414"),
            BoardShadowBlur = 36,
            StoneShadow = Color.Parse("#70050814"),
            Stones = StoneStyle.Pearl,
            Effect = PlacementEffect.GoldHalo,
            EffectColor = Color.Parse("#E8C36A"),
            LastMove = Color.Parse("#E8C36A"),
        },
    };

    public static HoshiTheme InkAndGold { get; } = new()
    {
        Id = "sumi",
        NameKey = "Theme.Sumi.Name",
        DescriptionKey = "Theme.Sumi.Description",
        Window = Color.Parse("#141518"),
        Bar = Color.Parse("#17181B"),
        Sidebar = Color.Parse("#101114"),
        Properties = Color.Parse("#151619"),
        PanelAlt = Color.Parse("#222428"),
        Border = Color.Parse("#2C2E33"),
        Text = Color.Parse("#ECE8E1"),
        TextSecondary = Color.Parse("#8F8A82"),
        Accent = Color.Parse("#C9A45C"),
        Danger = Color.Parse("#B23A2E"),
        EditBar = Color.Parse("#8E2C23"),
        EditBarText = Color.Parse("#F6EEE4"),
        EditBarHover = Color.Parse("#A7382E"),
        Surround = Color.Parse("#121316"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "ShipporiMincho#Shippori Mincho",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 15,
        IconSet = "PhosphorRegular",
        Background = BackgroundKind.InkMist,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/kaya-itame.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#E0B97A"),
            BorderWidth = 0,
            Lines = Color.Parse("#2B1D10"),
            Coordinates = Color.Parse("#A02B1D10"),
            BoardShadow = Color.Parse("#F0000000"),
            BoardShadowBlur = 40,
            StoneShadow = Color.Parse("#80000000"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.InkRipple,
            EffectColor = Color.Parse("#1A1A20"),
            LastMove = Color.Parse("#B23A2E"),
        },
    };

    public static HoshiTheme ZenGarden { get; } = new()
    {
        Id = "zen",
        NameKey = "Theme.Zen.Name",
        DescriptionKey = "Theme.Zen.Description",
        Window = Color.Parse("#262724"),
        Bar = Color.Parse("#2A2B28"),
        Sidebar = Color.Parse("#222320"),
        Properties = Color.Parse("#272825"),
        PanelAlt = Color.Parse("#34362F"),
        Border = Color.Parse("#3E403A"),
        Text = Color.Parse("#E9E6DC"),
        TextSecondary = Color.Parse("#9C9A8E"),
        Accent = Color.Parse("#9DBB7F"),
        EditBar = Color.Parse("#6F8A5B"),
        EditBarText = Color.Parse("#F4F2EA"),
        EditBarHover = Color.Parse("#7F9C69"),
        Surround = Color.Parse("#CFC8B8"),
        UiFont = Fonts + "ZenKakuGothicNew#Zen Kaku Gothic New",
        TitleFont = Fonts + "ZenKakuGothicNew#Zen Kaku Gothic New",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 15,
        IconSet = "Lucide",
        Background = BackgroundKind.ZenSand,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/shin-kaya.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#EFD8A4"),
            Border = Color.Parse("#B89A68"),
            BorderWidth = 0.06,
            Lines = Color.Parse("#4A3A22"),
            Coordinates = Color.Parse("#B04A3A22"),
            BoardShadow = Color.Parse("#8C3C3222"),
            BoardShadowBlur = 24,
            StoneShadow = Color.Parse("#5A2A2010"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.SandRipple,
            EffectColor = Color.Parse("#FFF8EC"),
        },
    };

    public static HoshiTheme WarmMinimal { get; } = new()
    {
        Id = "minimal",
        NameKey = "Theme.Minimal.Name",
        DescriptionKey = "Theme.Minimal.Description",
        Window = Color.Parse("#2A2624"),
        Bar = Color.Parse("#2B2826"),
        Sidebar = Color.Parse("#231F1D"),
        Properties = Color.Parse("#2A2624"),
        PanelAlt = Color.Parse("#37312E"),
        Border = Color.Parse("#403A36"),
        Text = Color.Parse("#F1ECE4"),
        TextSecondary = Color.Parse("#A39A90"),
        Accent = Color.Parse("#D07A4A"),
        EditBar = Color.Parse("#C0673A"),
        EditBarText = Color.Parse("#FFF6EE"),
        EditBarHover = Color.Parse("#D07A4A"),
        Surround = Color.Parse("#EFE9DD"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Manrope#Manrope",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 14,
        IconSet = "Tabler",
        Background = BackgroundKind.Washi,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/katsura.jpg",
            StoneSet = "ceramic",
            WhiteVariants = 2,
            BlackVariants = 2,
            Wood = Color.Parse("#E0AA72"),
            BorderWidth = 0,
            Lines = Color.Parse("#4B3828"),
            Coordinates = Color.Parse("#A04B3828"),
            BoardShadow = Color.Parse("#593C2814"),
            BoardShadowBlur = 18,
            StoneShadow = Color.Parse("#4D2A1C10"),
            Stones = StoneStyle.Soft,
            Effect = PlacementEffect.Settle,
        },
    };

    /// <summary>
    /// High contrast (accessibility): black chrome, white text, a yellow accent, a pale flat board with black lines,
    /// plain vector stones and a red last-move ring; nothing moves in the background.
    /// </summary>
    public static HoshiTheme HighContrast { get; } = new()
    {
        Id = "contrast",
        NameKey = "Theme.Contrast.Name",
        DescriptionKey = "Theme.Contrast.Description",
        Window = Color.Parse("#000000"),
        Bar = Color.Parse("#000000"),
        Sidebar = Color.Parse("#000000"),
        Properties = Color.Parse("#0A0A0A"),
        PanelAlt = Color.Parse("#1F1F1F"),
        Border = Color.Parse("#9A9A9A"),
        Text = Color.Parse("#FFFFFF"),
        TextSecondary = Color.Parse("#E6E6E6"),
        Accent = Color.Parse("#FFD400"),
        Danger = Color.Parse("#FF5C5C"),
        EditBar = Color.Parse("#0033CC"),
        EditBarText = Color.Parse("#FFFFFF"),
        EditBarHover = Color.Parse("#1F4FFF"),
        Surround = Color.Parse("#000000"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Manrope#Manrope",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "PhosphorRegular",
        Background = BackgroundKind.Slate,
        Board = new BoardStyle
        {
            Wood = Color.Parse("#F3E3B5"),
            Border = Color.Parse("#000000"),
            BorderWidth = 0.08,
            Lines = Color.Parse("#000000"),
            Coordinates = Color.Parse("#FF000000"),
            BoardShadow = Colors.Transparent,
            BoardShadowBlur = 0,
            StoneShadow = Colors.Transparent,
            Stones = StoneStyle.Gradient,
            Effect = PlacementEffect.None,
            LastMove = Color.Parse("#FF1E1E"),
        },
    };

    /// <summary>
    /// Kaya (light, from the 2026-10 redesign): cream paper, ink text, an indigo edit bar and a seal-red accent,
    /// on washi with a pale shin-kaya board.
    /// </summary>
    public static HoshiTheme Kaya { get; } = new()
    {
        Id = "kaya",
        NameKey = "Theme.Kaya.Name",
        DescriptionKey = "Theme.Kaya.Description",
        IsLight = true,
        Window = Color.Parse("#F1E6CF"),
        Bar = Color.Parse("#EADCBF"),
        Sidebar = Color.Parse("#F6EEDC"),
        Properties = Color.Parse("#FBF6EA"),
        PanelAlt = Color.Parse("#E6D6B6"),
        Border = Color.Parse("#CDBB97"),
        Text = Color.Parse("#1E1A14"),
        TextSecondary = Color.Parse("#6B5F48"),
        Accent = Color.Parse("#B23A2A"),
        Danger = Color.Parse("#B23A2A"),
        Good = Color.Parse("#2F7A4E"),
        EditBar = Color.Parse("#26357F"),
        EditBarText = Color.Parse("#F6EEDC"),
        EditBarHover = Color.Parse("#34449A"),
        Surround = Color.Parse("#EFE6D2"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "ShipporiMincho#Shippori Mincho",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "PhosphorRegular",
        Background = BackgroundKind.Washi,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/shin-kaya.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#E9CF98"),
            Border = Color.Parse("#B89A68"),
            BorderWidth = 0.04,
            Lines = Color.Parse("#3A2A14"),
            Coordinates = Color.Parse("#B03A2A14"),
            BoardShadow = Color.Parse("#663C2814"),
            BoardShadowBlur = 22,
            StoneShadow = Color.Parse("#552A1C10"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.InkRipple,
            EffectColor = Color.Parse("#1E1A14"),
            LastMove = Color.Parse("#B23A2A"),
        },
    };

    /// <summary>
    /// Mochi (light, the default since 2026-10-18; the website's look): warm cream, a plum ink, a rose accent,
    /// pastel tints, rounded Fredoka titles, Lucide's round icons, soft corners and a pale shin-kaya board.
    /// Friendly, not childish: the board and the text stay calm.
    /// </summary>
    public static HoshiTheme Mochi { get; } = new()
    {
        Id = "mochi",
        NameKey = "Theme.Mochi.Name",
        DescriptionKey = "Theme.Mochi.Description",
        IsLight = true,
        Rounded = true,
        Window = Color.Parse("#F8F1E7"),
        Bar = Color.Parse("#FFFAF3"),
        Sidebar = Color.Parse("#FFFAF3"),
        Properties = Color.Parse("#FFFFFF"),
        PanelAlt = Color.Parse("#F7E8EE"),
        Border = Color.Parse("#EBDDCC"),
        Text = Color.Parse("#2D2440"),
        TextSecondary = Color.Parse("#7B6F88"),
        Accent = Color.Parse("#C8577A"),
        Danger = Color.Parse("#C8432F"),
        Good = Color.Parse("#3E8E6A"),
        EditBar = Color.Parse("#2D2440"),
        EditBarText = Color.Parse("#FFF8F0"),
        EditBarHover = Color.Parse("#43385C"),
        Surround = Color.Parse("#F8F1E7"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Fredoka#Fredoka",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "Lucide",
        Background = BackgroundKind.Mochi,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/shin-kaya.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#EBD3A0"),
            Border = Color.Parse("#C9A876"),
            BorderWidth = 0.04,
            Lines = Color.Parse("#3D2C1A"),
            Coordinates = Color.Parse("#A03D2C1A"),
            BoardShadow = Color.Parse("#4D7A5A46"),
            BoardShadowBlur = 30,
            StoneShadow = Color.Parse("#4A2D2440"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.Settle,
            EffectColor = Color.Parse("#C8577A"),
            LastMove = Color.Parse("#C8577A"),
        },
    };

    /// <summary>Matcha (light, Mochi's family): tea green and cream, a deep green ink, a katsura board.</summary>
    public static HoshiTheme Matcha { get; } = new()
    {
        Id = "matcha",
        NameKey = "Theme.Matcha.Name",
        DescriptionKey = "Theme.Matcha.Description",
        IsLight = true,
        Rounded = true,
        Window = Color.Parse("#EEF3E6"),
        Bar = Color.Parse("#F8FBF2"),
        Sidebar = Color.Parse("#F8FBF2"),
        Properties = Color.Parse("#FFFFFF"),
        PanelAlt = Color.Parse("#E3EED7"),
        Border = Color.Parse("#D2DEC3"),
        Text = Color.Parse("#22302A"),
        TextSecondary = Color.Parse("#62726A"),
        Accent = Color.Parse("#5B8C48"),
        Danger = Color.Parse("#C0533A"),
        Good = Color.Parse("#3B7F58"),
        EditBar = Color.Parse("#22302A"),
        EditBarText = Color.Parse("#F8FBF2"),
        EditBarHover = Color.Parse("#33463C"),
        Surround = Color.Parse("#EEF3E6"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Fredoka#Fredoka",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "Lucide",
        Background = BackgroundKind.Matcha,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/katsura.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#DDBB84"),
            Border = Color.Parse("#B8955E"),
            BorderWidth = 0.04,
            Lines = Color.Parse("#36281A"),
            Coordinates = Color.Parse("#A036281A"),
            BoardShadow = Color.Parse("#4D3C5A3A"),
            BoardShadowBlur = 30,
            StoneShadow = Color.Parse("#4A22302A"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.Settle,
            EffectColor = Color.Parse("#5B8C48"),
            LastMove = Color.Parse("#5B8C48"),
        },
    };

    /// <summary>Yuzu (light, Mochi's family): warm butter and peach, a citrus accent, a pale kaya board.</summary>
    public static HoshiTheme Yuzu { get; } = new()
    {
        Id = "yuzu",
        NameKey = "Theme.Yuzu.Name",
        DescriptionKey = "Theme.Yuzu.Description",
        IsLight = true,
        Rounded = true,
        Window = Color.Parse("#FBF3DF"),
        Bar = Color.Parse("#FFFAEC"),
        Sidebar = Color.Parse("#FFFAEC"),
        Properties = Color.Parse("#FFFFFF"),
        PanelAlt = Color.Parse("#F8E8C2"),
        Border = Color.Parse("#EAD9AE"),
        Text = Color.Parse("#3A2E1F"),
        TextSecondary = Color.Parse("#85735A"),
        Accent = Color.Parse("#D97E22"),
        Danger = Color.Parse("#C8432F"),
        Good = Color.Parse("#4B8749"),
        EditBar = Color.Parse("#3A2E1F"),
        EditBarText = Color.Parse("#FFFAEC"),
        EditBarHover = Color.Parse("#4E3F2C"),
        Surround = Color.Parse("#FBF3DF"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Fredoka#Fredoka",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "Lucide",
        Background = BackgroundKind.Yuzu,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/kaya-itame.jpg",
            StoneSet = "clam-slate",
            WhiteVariants = 8,
            BlackVariants = 4,
            Wood = Color.Parse("#E6C68C"),
            Border = Color.Parse("#C49F62"),
            BorderWidth = 0.04,
            Lines = Color.Parse("#3B2A16"),
            Coordinates = Color.Parse("#A03B2A16"),
            BoardShadow = Color.Parse("#4D7A5A30"),
            BoardShadowBlur = 30,
            StoneShadow = Color.Parse("#4A3A2E1F"),
            Stones = StoneStyle.SlateShell,
            Effect = PlacementEffect.Settle,
            EffectColor = Color.Parse("#D97E22"),
            LastMove = Color.Parse("#D97E22"),
        },
    };

    /// <summary>Kuro mochi (dark, Mochi's family): a deep plum night, a soft pink accent, round corners, glass stones.</summary>
    public static HoshiTheme MochiNight { get; } = new()
    {
        Id = "kuromochi",
        NameKey = "Theme.KuroMochi.Name",
        DescriptionKey = "Theme.KuroMochi.Description",
        Rounded = true,
        Window = Color.Parse("#1C1828"),
        Bar = Color.Parse("#231E33"),
        Sidebar = Color.Parse("#231E33"),
        Properties = Color.Parse("#2A2440"),
        PanelAlt = Color.Parse("#352D4F"),
        Border = Color.Parse("#3E3658"),
        Text = Color.Parse("#F3EDF7"),
        TextSecondary = Color.Parse("#B3A8C6"),
        Accent = Color.Parse("#F0A3BC"),
        Danger = Color.Parse("#F0786A"),
        Good = Color.Parse("#8FD6B0"),
        EditBar = Color.Parse("#4A3D6E"),
        EditBarText = Color.Parse("#F3EDF7"),
        EditBarHover = Color.Parse("#5A4C82"),
        Surround = Color.Parse("#1C1828"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Fredoka#Fredoka",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 16,
        IconSet = "Lucide",
        Background = BackgroundKind.MochiNight,
        Board = new BoardStyle
        {
            Texture = "avares://Hoshi.App/Assets/Art/Boards/kaya-masame.jpg",
            StoneSet = "glass",
            WhiteVariants = 3,
            BlackVariants = 3,
            Wood = Color.Parse("#E8C483"),
            Border = Color.Parse("#C9A45C"),
            BorderWidth = 0.05,
            Lines = Color.Parse("#3B2A14"),
            Coordinates = Color.Parse("#B03B2A14"),
            BoardShadow = Color.Parse("#E6080614"),
            BoardShadowBlur = 36,
            StoneShadow = Color.Parse("#70080614"),
            Stones = StoneStyle.Pearl,
            Effect = PlacementEffect.Settle,
            EffectColor = Color.Parse("#F0A3BC"),
            LastMove = Color.Parse("#F0A3BC"),
        },
    };

    public static IReadOnlyList<HoshiTheme> All { get; } = [Mochi, Matcha, Yuzu, MochiNight, NightSky, Kaya, InkAndGold, ZenGarden, WarmMinimal, HighContrast];

    /// <summary>
    /// Mochi on desktops (2026-10-18). Phones keep Pole star until their own Mochi pass (their launch screen is
    /// night blue, so a light first frame would flash).
    /// </summary>
    public static HoshiTheme Default => Services.PlatformInfo.IsPhone ? NightSky : Mochi;

    public static HoshiTheme ById(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Default;
}
