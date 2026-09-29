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
    private static readonly Dictionary<Color, Bitmap> Kaya = [];

    /// <summary>Shudan's board texture (Classic theme).</summary>
    public static Bitmap Wood => LazyWood.Value;

    /// <summary>Hoshi's own procedural kaya, tinted to <paramref name="baseColor"/> (cached per colour).</summary>
    public static Bitmap KayaFor(Color baseColor)
    {
        lock (Kaya)
        {
            if (!Kaya.TryGetValue(baseColor, out Bitmap? bitmap))
            {
                bitmap = CreateWood(baseColor);
                Kaya[baseColor] = bitmap;
            }

            return bitmap;
        }
    }

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

    // ---- Hoshi's own stone styles ----

    private static IBrush Radial(double ox, double oy, double radius, params (Color Color, double Offset)[] stops)
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(ox, oy, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
        };
        foreach ((Color c, double o) in stops)
        {
            b.GradientStops.Add(new GradientStop(c, o));
        }

        return b.ToImmutable();
    }

    public static IBrush PearlBlack { get; } = Radial(0.32, 0.26, 0.62,
        (Color.Parse("#5A6178"), 0), (Color.Parse("#1C1F2A"), 0.38), (Color.Parse("#07080C"), 1));

    public static IBrush PearlWhite { get; } = Radial(0.34, 0.28, 0.66,
        (Color.Parse("#FFFFFF"), 0), (Color.Parse("#F2F1F6"), 0.5), (Color.Parse("#CFD3E2"), 0.92), (Color.Parse("#E9DDBF"), 1));

    public static IBrush SlateBlack { get; } = Radial(0.36, 0.3, 0.64,
        (Color.Parse("#4A4A48"), 0), (Color.Parse("#232322"), 0.42), (Color.Parse("#0C0C0B"), 1));

    public static IBrush ShellWhite { get; } = Radial(0.36, 0.3, 0.66,
        (Color.Parse("#FFFEFA"), 0), (Color.Parse("#F1EDE3"), 0.55), (Color.Parse("#CFC8B7"), 1));

    public static IBrush SoftBlack { get; } = Radial(0.4, 0.34, 0.7,
        (Color.Parse("#3C3835"), 0), (Color.Parse("#191716"), 1));

    public static IBrush SoftWhite { get; } = Radial(0.4, 0.34, 0.72,
        (Color.Parse("#FFFDF8"), 0), (Color.Parse("#E5DED1"), 1));

    public static IPen ShellLine { get; } = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x1C, 0x6A, 0x5E, 0x46)), 1);

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
            return CreateWood(Color.FromRgb(0xE0, 0xB9, 0x68));
        }
    }

    private static Bitmap CreateWood(Color baseColor)
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

                // Masame (straight-grain) kaya: thin, slightly irregular dark lines running top to bottom.
                double thin = Math.Pow(Math.Abs(Math.Sin(((u * 150.0) + (warp * 0.7) + (Fbm(u * 8, v * 0.3, 2) * 1.5)) * Math.PI)), 14);
                double shade = 1.0 + (0.02 * grain) + (0.03 * fine) + (0.045 * blotch) - (0.075 * thin);

                double r = baseColor.R * shade;
                double g = baseColor.G * shade;
                double b = baseColor.B * shade;

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
