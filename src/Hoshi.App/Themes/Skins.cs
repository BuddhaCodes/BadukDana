using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hoshi.Core.Localization;

namespace Hoshi.App.Themes;

/// <summary>A goban the player can pick independently of the theme (Preferences → Appearance).</summary>
/// <param name="Texture">An <c>avares://</c> image of the wood, or null for Hoshi's procedural kaya.</param>
/// <param name="Wood">Average colour of the wood (labels and markers are drawn on it).</param>
public sealed record BoardSkin(
    string Id, string NameKey, string? Texture, Color Wood, Color Lines, Color Coordinates, Color Border, double BorderWidth)
{
    public string Name => Tr.T(NameKey);
}

/// <summary>A set of stones: Hoshi's rendered sprites (several variants per colour) or one of the vector styles.</summary>
public sealed record StoneSkin(string Id, string NameKey, StoneStyle Vector, string? Set = null, int WhiteVariants = 0, int BlackVariants = 0)
{
    public string Name => Tr.T(NameKey);
}

/// <summary>What is drawn around the board.</summary>
public sealed record BackgroundSkin(string Id, string NameKey, BackgroundKind Kind, Color Surround, string? Tile = null)
{
    public string Name => Tr.T(NameKey);
}

/// <summary>
/// The boards, stones and backgrounds Hoshi ships. The art in <c>Assets/Art</c> is rendered by
/// <c>tools/art/generate.py</c> (Hoshi's own, no third-party images); Shudan's board and tatami are Sabaki's (MIT).
/// </summary>
public static class Skins
{
    private const string Art = "avares://Hoshi/Assets/Art/";

    public static IReadOnlyList<BoardSkin> Boards { get; } =
    [
        new("kaya-masame", "Skin.Board.KayaMasame", Art + "Boards/kaya-masame.jpg", C("#E8C483"), C("#3E2A12"), C("#B03E2A12"), C("#C9A15C"), 0.05),
        new("kaya-itame", "Skin.Board.KayaItame", Art + "Boards/kaya-itame.jpg", C("#E0B97A"), C("#3A2610"), C("#B03A2610"), C("#B98D4E"), 0.05),
        new("shin-kaya", "Skin.Board.ShinKaya", Art + "Boards/shin-kaya.jpg", C("#EFD8A4"), C("#4A3820"), C("#B04A3820"), C("#D2B67C"), 0.05),
        new("katsura", "Skin.Board.Katsura", Art + "Boards/katsura.jpg", C("#E0AA72"), C("#3C220E"), C("#B03C220E"), C("#B98150"), 0.05),
        new("bamboo", "Skin.Board.Bamboo", Art + "Boards/bamboo.jpg", C("#D1AB71"), C("#3A2812"), C("#B03A2812"), C("#A88350"), 0.06),
        new("walnut", "Skin.Board.Walnut", Art + "Boards/walnut.jpg", C("#63442F"), C("#EADCC0"), C("#C0EADCC0"), C("#3E2A1C"), 0.06),
        new("sabaki", "Skin.Board.Sabaki", "avares://Hoshi/Assets/Sabaki/board.png", C("#F1B458"), C("#5E2E0C"), C("#D05E2E0C"), C("#CA933A"), 0.15),
        new("kaya", "Skin.Board.Kaya", null, C("#E3C28A"), C("#5E2E0C"), C("#D05E2E0C"), C("#CA933A"), 0.05),
    ];

    public static IReadOnlyList<StoneSkin> Stones { get; } =
    [
        new("clam-slate", "Skin.Stones.ClamSlate", StoneStyle.SlateShell, "clam-slate", 8, 4),
        new("yunzi", "Skin.Stones.Yunzi", StoneStyle.Soft, "yunzi", 4, 4),
        new("glass", "Skin.Stones.Glass", StoneStyle.Pearl, "glass", 3, 3),
        new("ceramic", "Skin.Stones.Ceramic", StoneStyle.Soft, "ceramic", 2, 2),
        new("jade", "Skin.Stones.Jade", StoneStyle.Pearl, "jade", 4, 4),
        new("pearl", "Skin.Stones.Pearl", StoneStyle.Pearl),
        new("soft", "Skin.Stones.Soft", StoneStyle.Soft),
        new("shudan", "Skin.Stones.Shudan", StoneStyle.Shudan),
    ];

    public static IReadOnlyList<BackgroundSkin> Backgrounds { get; } =
    [
        new("night", "Skin.Bg.Night", BackgroundKind.NightSky, C("#0B1020")),
        new("ink", "Skin.Bg.Ink", BackgroundKind.InkMist, C("#121316")),
        new("zen", "Skin.Bg.Zen", BackgroundKind.ZenSand, C("#CFC8B8")),
        new("sashiko", "Skin.Bg.Sashiko", BackgroundKind.Sashiko, C("#232D47"), Art + "Backgrounds/sashiko.jpg"),
        new("walnut", "Skin.Bg.Walnut", BackgroundKind.WalnutTable, C("#422B1E"), Art + "Backgrounds/walnut.jpg"),
        new("slate", "Skin.Bg.Slate", BackgroundKind.Slate, C("#272A30"), Art + "Backgrounds/slate.jpg"),
        new("linen", "Skin.Bg.Linen", BackgroundKind.Linen, C("#2C2D32"), Art + "Backgrounds/linen.jpg"),
        new("sudare", "Skin.Bg.Sudare", BackgroundKind.Sudare, C("#B08E5A"), Art + "Backgrounds/sudare.jpg"),
        new("washi", "Skin.Bg.Washi", BackgroundKind.Washi, C("#EFE9DD")),
        new("tatami", "Skin.Bg.Tatami", BackgroundKind.Tatami, C("#C2CB9C"), "avares://Hoshi/Assets/Sabaki/tatami.png"),
    ];

    public static BoardSkin? Board(string? id) => Boards.FirstOrDefault(b => b.Id == id);

    public static StoneSkin? StoneSet(string? id) => Stones.FirstOrDefault(s => s.Id == id);

    public static BackgroundSkin? Background(string? id) => Backgrounds.FirstOrDefault(b => b.Id == id);

    public static BackgroundSkin? Background(BackgroundKind kind) => Backgrounds.FirstOrDefault(b => b.Kind == kind);

    /// <summary>The theme's board with the player's choice of goban and stones on top (null = the theme's own).</summary>
    public static BoardStyle Compose(BoardStyle theme, BoardSkin? board, StoneSkin? stones)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (board is null && stones is null)
        {
            return theme;
        }

        BoardStyle style = Expand(theme);
        if (board is not null)
        {
            style = style with
            {
                Texture = board.Texture,
                Wood = board.Wood,
                Lines = board.Lines,
                Coordinates = board.Coordinates,
                Border = board.Border,
                BorderWidth = board.BorderWidth,
            };
        }

        if (stones is not null)
        {
            style = style with { Stones = stones.Vector, StoneSet = stones.Set, WhiteVariants = stones.WhiteVariants, BlackVariants = stones.BlackVariants };
        }

        return style;
    }

    /// <summary>Classic's "Shudan" shortcut spelled out, so its board and stones can be changed one at a time.</summary>
    public static BoardStyle Expand(BoardStyle s) => !s.ShudanTexture ? s : s with
    {
        ShudanTexture = false,
        Texture = "avares://Hoshi/Assets/Sabaki/board.png",
        Wood = C("#F1B458"),
        Border = C("#CA933A"),
        BorderWidth = 0.15,
        Lines = C("#5E2E0C"),
        Coordinates = C("#D05E2E0C"),
        StoneShadow = C("#66170A02"),
        Stones = StoneStyle.Shudan,
    };

    private static readonly Dictionary<string, Bitmap?> Cache = [];

    /// <summary>Loads (once) an image shipped with Hoshi; null when it cannot be read.</summary>
    public static Bitmap? Image(string? uri)
    {
        if (uri is null)
        {
            return null;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue(uri, out Bitmap? bitmap))
            {
                try
                {
                    using Stream s = AssetLoader.Open(new Uri(uri));
                    bitmap = new Bitmap(s);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException or ArgumentException)
                {
                    bitmap = null;
                }

                Cache[uri] = bitmap;
            }

            return bitmap;
        }
    }

    /// <summary>The sprite of one stone of a set: the variant is stable per intersection (it never flickers).</summary>
    public static Bitmap? StoneSprite(string set, bool black, int variants, int x, int y)
    {
        if (variants <= 0)
        {
            return null;
        }

        int hash = unchecked((x * 73856093) ^ (y * 19349663) ^ (black ? 83492791 : 0));
        int i = (int)((uint)hash % (uint)variants);
        return Image($"{Art}Stones/{set}/{(black ? 'b' : 'w')}{i}.png");
    }

    private static Color C(string s) => Color.Parse(s);
}
