using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hoshi.App.Controls;

/// <summary>
/// Board and stone look, after Sabaki's board component Shudan (MIT, see THIRD_PARTY_NOTICES.md): the board uses
/// Shudan's <c>board.png</c> over <c>#F1B458</c>, and the stones reproduce the gradients of its
/// <c>stone_1.svg</c> / <c>stone_-1.svg</c>. The procedural wood below is only a fallback if the asset cannot be loaded.
/// </summary>
internal static class BoardTextures
{
    public const int WoodSize = 512;

    public static readonly Color BoardBackground = Color.FromRgb(0xF1, 0xB4, 0x58);
    public static readonly Color BoardBorder = Color.FromRgb(0xCA, 0x93, 0x3A);
    public static readonly Color BoardForeground = Color.FromRgb(0x5E, 0x2E, 0x0C);

    private static readonly Lazy<Bitmap> LazyWood = new(LoadBoard);

    public static Bitmap Wood => LazyWood.Value;

    /// <summary>Stone body: brownish slate at the top to near-black at the bottom (Shudan stone_1.svg).</summary>
    public static IBrush BlackStone { get; } = Vertical(
        (0, Color.FromRgb(0x44, 0x34, 0x32)), (1, Color.FromRgb(0x0B, 0x0B, 0x0B)));

    /// <summary>Soft grey sheen over the upper half of black stones.</summary>
    public static IBrush BlackHighlight { get; } = Vertical(
        (0, Color.FromArgb(0x66, 0x63, 0x63, 0x63)), (0.44, Color.FromArgb(0x00, 0x63, 0x63, 0x63)));

    public static IPen BlackEdge { get; } = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Black), 1);

    /// <summary>White body: pure white at the top to a cool blue-white at the bottom (Shudan stone_-1.svg).</summary>
    public static IBrush WhiteStone { get; } = Vertical(
        (0, Color.FromRgb(0xFF, 0xFF, 0xFF)), (1, Color.FromRgb(0xC9, 0xD1, 0xFF)));

    /// <summary>Reflected light along the lower rim of white stones.</summary>
    public static IBrush WhiteHighlight { get; } = Vertical(
        (0.747, Color.FromArgb(0x00, 0xEE, 0xEE, 0xEE)), (1, Color.FromArgb(0xCC, 0xEE, 0xEE, 0xEE)));

    public static IPen WhiteEdge { get; } = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xC3, 0xC3, 0xC3)), 1);

    /// <summary>The dark disc under every stone and its drop shadow (Shudan: rgba(23,10,2,.4), 0 .1em .2em).</summary>
    public static readonly Color ShadowColor = Color.FromArgb(0x66, 23, 10, 2);

    private static IBrush Vertical((double Offset, Color Color) top, (double Offset, Color Color) bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(top.Color, top.Offset),
            new GradientStop(bottom.Color, bottom.Offset),
        },
    }.ToImmutable();

    private static Bitmap LoadBoard()
    {
        try
        {
            using Stream s = AssetLoader.Open(new Uri("avares://Hoshi/Assets/Sabaki/board.png"));
            return new Bitmap(s);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
        {
            return CreateWood();
        }
    }

    private static Bitmap CreateWood()
    {
        const int n = WoodSize;
        var pixels = new int[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                double u = x / (double)n;
                double v = y / (double)n;

                // Vertical grain: long stretched noise, gently warped, so lines run top to bottom without strong figure.
                double warp = Fbm(u * 3.0, v * 0.6, 3) * 2.2;
                double grain = Math.Sin(((u * 38.0) + warp) * Math.PI);
                double fine = Fbm(u * 60.0, v * 4.0, 2) - 0.5;
                double blotch = Fbm(u * 2.0, v * 2.0, 3) - 0.5;

                double shade = 1.0 + (0.022 * grain) + (0.035 * fine) + (0.05 * blotch);

                // Base kaya colour (DESIGN.md Board.Wood #DCB35C), slightly lighter.
                double r = 0xE0 * shade;
                double g = 0xB9 * shade;
                double b = 0x68 * shade;

                pixels[(y * n) + x] = unchecked((int)0xFF000000)
                    | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
            }
        }

        var bitmap = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer fb = bitmap.Lock())
        {
            for (int row = 0; row < n; row++)
            {
                Marshal.Copy(pixels, row * n, fb.Address + (row * fb.RowBytes), n);
            }
        }

        return bitmap;
    }

    private static int Clamp(double c) => (int)Math.Clamp(Math.Round(c), 0, 255);

    private static double Fbm(double x, double y, int octaves)
    {
        double sum = 0;
        double amp = 0.5;
        double freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * freq, y * freq);
            freq *= 2;
            amp *= 0.5;
        }

        return sum / (1 - Math.Pow(0.5, octaves));
    }

    private static double ValueNoise(double x, double y)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        double u = xf * xf * (3 - (2 * xf));
        double w = yf * yf * (3 - (2 * yf));

        double a = Hash(xi, yi);
        double b = Hash(xi + 1, yi);
        double c = Hash(xi, yi + 1);
        double d = Hash(xi + 1, yi + 1);
        return a + ((b - a) * u) + ((c - a) * w) + ((a - b - c + d) * u * w);
    }

    private static double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (y * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
}
