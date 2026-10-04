using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.Core;

namespace Hoshi.App.Themes;

/// <summary>
/// Small pictures of gobans, stone sets and backgrounds for the pickers in Preferences, drawn with the same code as
/// the board itself (so vector stones and animated backgrounds get one too). Cached; null when nothing can be drawn
/// (e.g. before Avalonia is running).
/// </summary>
public static class SkinThumbnails
{
    private const int Size = 64;
    private static readonly Dictionary<string, IImage?> Cache = [];

    /// <summary>The wood of a board style (its texture, Shudan's board or Hoshi's tinted kaya).</summary>
    public static IImage? Board(BoardStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.ShudanTexture
            ? Skins.Image("avares://Hoshi/Assets/Sabaki/board.png")
            : Skins.Image(style.Texture) ?? Cached("kaya:" + style.Wood, () => BoardTextures.KayaFor(style.Wood));
    }

    /// <summary>A white and a black stone side by side (twice as wide as tall).</summary>
    public static IImage? Stones(BoardStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        string key = $"stones:{style.ShudanTexture}:{style.Stones}:{style.StoneSet}";
        return Cached(key, () =>
        {
            var bitmap = new RenderTargetBitmap(new PixelSize(Size * 2, Size), new Vector(96, 96));
            using (DrawingContext ctx = bitmap.CreateDrawingContext())
            {
                double r = Size * 0.44;
                GoBoardControl.DrawStone(ctx, new Avalonia.Point(Size * 0.5, Size * 0.5), r, Stone.White, style, new Hoshi.Core.Point(0, 0));
                GoBoardControl.DrawStone(ctx, new Avalonia.Point(Size * 1.5, Size * 0.5), r, Stone.Black, style, new Hoshi.Core.Point(1, 0));
            }

            return bitmap;
        });
    }

    /// <summary>A still frame of a background (animated ones included).</summary>
    public static IImage? Background(BackgroundKind kind)
    {
        if (Skins.Background(kind)?.Tile is { } tile)
        {
            return Skins.Image(tile);
        }

        return Cached("bg:" + kind, () =>
        {
            var control = new ThemeBackground { Kind = kind, Animate = false, Width = Size * 2, Height = Size * 2 };
            control.Measure(new Size(Size * 2, Size * 2));
            control.Arrange(new Rect(0, 0, Size * 2, Size * 2));
            var bitmap = new RenderTargetBitmap(new PixelSize(Size * 2, Size * 2), new Vector(96, 96));
            bitmap.Render(control);
            return bitmap;
        });
    }

    private static IImage? Cached(string key, Func<IImage> create)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out IImage? image))
            {
                return image;
            }

            try
            {
                image = create();
                Cache[key] = image;
                return image;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                return null; // Avalonia is not running (tests without a UI); not cached, so it is tried again later
            }
        }
    }
}
