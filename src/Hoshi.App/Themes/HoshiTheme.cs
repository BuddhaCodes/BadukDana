using Avalonia.Media;

namespace Hoshi.App.Themes;

/// <summary>What is drawn around the board.</summary>
public enum BackgroundKind
{
    /// <summary>Sabaki's tatami (static).</summary>
    Tatami,

    /// <summary>Night sky: slow twinkling stars, drifting nebula, faint constellations, a rare shooting star.</summary>
    NightSky,

    /// <summary>Ink wash: soft sumi-e clouds that drift and dissolve over dark paper.</summary>
    InkMist,

    /// <summary>Raked sand of a zen garden, with ripples around two stones, moving very slowly.</summary>
    ZenSand,

    /// <summary>Washi paper with fibres (static).</summary>
    Washi,
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
    /// <summary>Shudan's gradients (Classic).</summary>
    Shudan,

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
    /// <summary>Use Shudan's board.png (Classic); otherwise Hoshi's procedural kaya tinted with <see cref="Wood"/>.</summary>
    public bool ShudanTexture { get; init; }

    public Color Wood { get; init; } = Color.Parse("#E3C28A");

    public Color Border { get; init; } = Color.Parse("#CA933A");

    /// <summary>Border width in cells (0 = none).</summary>
    public double BorderWidth { get; init; } = 0.15;

    public Color Lines { get; init; } = Color.Parse("#5E2E0C");

    public Color Coordinates { get; init; } = Color.Parse("#D05E2E0C");

    public Color BoardShadow { get; init; } = Color.Parse("#CC14000F");

    public double BoardShadowBlur { get; init; } = 20;

    public Color StoneShadow { get; init; } = Color.Parse("#66170A02");

    public StoneStyle Stones { get; init; } = StoneStyle.Shudan;

    public PlacementEffect Effect { get; init; } = PlacementEffect.None;

    public Color EffectColor { get; init; } = Colors.Transparent;

    /// <summary>Colour of the last-move ring; transparent = the opposite of the stone's colour.</summary>
    public Color LastMove { get; init; } = Colors.Transparent;
}

/// <summary>A complete look for Hoshi: chrome palette, fonts, icons, background and board.</summary>
public sealed record HoshiTheme
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

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
    private const string Fonts = "avares://Hoshi/Assets/Fonts/";

    public static HoshiTheme NightSky { get; } = new()
    {
        Id = "night",
        Name = "Cielo nocturno",
        Description = "Hoshi significa «estrella»: cielo azul noche con estrellas que titilan y oro pálido.",
        Window = Color.Parse("#0E1326"),
        Bar = Color.Parse("#10152A"),
        Sidebar = Color.Parse("#0B1020"),
        Properties = Color.Parse("#10162B"),
        PanelAlt = Color.Parse("#1B2342"),
        Border = Color.Parse("#26304F"),
        Text = Color.Parse("#E8E6F0"),
        TextSecondary = Color.Parse("#8E93AD"),
        Accent = Color.Parse("#D8B46A"),
        EditBar = Color.Parse("#2E3F73"),
        EditBarText = Color.Parse("#F1EEDF"),
        EditBarHover = Color.Parse("#3B4E8A"),
        Surround = Color.Parse("#0B1020"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "CormorantGaramond#Cormorant Garamond",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        TitleSize = 18,
        IconSet = "PhosphorLight",
        Background = BackgroundKind.NightSky,
        Board = new BoardStyle
        {
            Wood = Color.Parse("#E6C68E"),
            Border = Color.Parse("#C9A45C"),
            BorderWidth = 0.05,
            Lines = Color.Parse("#3B2A14"),
            Coordinates = Color.Parse("#B03B2A14"),
            BoardShadow = Color.Parse("#E6000414"),
            BoardShadowBlur = 36,
            StoneShadow = Color.Parse("#70050814"),
            Stones = StoneStyle.Pearl,
            Effect = PlacementEffect.GoldHalo,
            EffectColor = Color.Parse("#E8C77A"),
            LastMove = Color.Parse("#D8B46A"),
        },
    };

    public static HoshiTheme InkAndGold { get; } = new()
    {
        Id = "sumi",
        Name = "Tinta y oro",
        Description = "Nocturno sumi-e: niebla de tinta que se mueve despacio, oro y rojo sello.",
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
            Wood = Color.Parse("#DDB579"),
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
        Name = "Jardín zen",
        Description = "Arena rastrillada que se mueve muy despacio, tonos piedra y verde musgo.",
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
            Wood = Color.Parse("#E6CC98"),
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
        Name = "Minimal cálido",
        Description = "Sobrio: papel washi, madera clara y terracota, con micro-animaciones.",
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
            Wood = Color.Parse("#EACF9E"),
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

    public static HoshiTheme Classic { get; } = new()
    {
        Id = "classic",
        Name = "Clásico",
        Description = "Homenaje a Sabaki: tatami, madera de Shudan e interfaz gris oscura.",
        Window = Color.Parse("#1E1E1E"),
        Bar = Color.Parse("#292A2D"),
        Sidebar = Color.Parse("#111111"),
        Properties = Color.Parse("#181818"),
        PanelAlt = Color.Parse("#2D2D30"),
        Border = Color.Parse("#3A3A3A"),
        Text = Color.Parse("#E6E6E6"),
        TextSecondary = Color.Parse("#9A9A9A"),
        Accent = Color.Parse("#E0A94A"),
        EditBar = Color.Parse("#C4BD64"),
        EditBarText = Color.Parse("#222222"),
        EditBarHover = Color.Parse("#B3AC53"),
        Surround = Color.Parse("#C2CB9C"),
        UiFont = Fonts + "Manrope#Manrope",
        TitleFont = Fonts + "Manrope#Manrope",
        MonoFont = Fonts + "JetBrainsMono#JetBrains Mono",
        IconSet = "Lucide",
        Background = BackgroundKind.Tatami,
        Board = new BoardStyle { ShudanTexture = true },
    };

    public static IReadOnlyList<HoshiTheme> All { get; } = [NightSky, InkAndGold, ZenGarden, WarmMinimal, Classic];

    public static HoshiTheme Default => NightSky;

    public static HoshiTheme ById(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Default;
}
