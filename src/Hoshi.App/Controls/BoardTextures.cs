using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hoshi.App.Controls;

/// <summary>
/// Procedurally generated textures (our own, no third-party assets): a light kaya-like wood and the stone brushes.
/// Generated once and cached; everything here is deterministic.
/// </summary>
internal static class BoardTextures
{
    public const int WoodSize = 512;

    private static readonly Lazy<Bitmap> LazyWood = new(CreateWood);

    public static Bitmap Wood => LazyWood.Value;

    public static IBrush BlackStone { get; } = new RadialGradientBrush
    {
        GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
        Center = new RelativePoint(0.45, 0.42, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.62, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.62, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0x5E, 0x5E, 0x5E), 0),
            new GradientStop(Color.FromRgb(0x2B, 0x2B, 0x2B), 0.35),
            new GradientStop(Color.FromRgb(0x0E, 0x0E, 0x0E), 1),
        },
    }.ToImmutable();

    public static IBrush WhiteStone { get; } = new RadialGradientBrush
    {
        GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
        Center = new RelativePoint(0.45, 0.42, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.65, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.65, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Color.FromRgb(0xEE, 0xEC, 0xE6), 0.55),
            new GradientStop(Color.FromRgb(0xC6, 0xC3, 0xB8), 1),
        },
    }.ToImmutable();

    public static IBrush Shadow { get; } = new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0.55),
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
        },
    }.ToImmutable();

    /// <summary>Faint shell veins for white stones (5 variants, chosen by point hash).</summary>
    public static IPen ShellVein { get; } = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x16, 0x5A, 0x50, 0x3C)), 1);

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
