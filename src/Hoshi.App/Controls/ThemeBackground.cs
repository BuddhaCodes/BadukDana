using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using Hoshi.App.Themes;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// The backdrop around the board, drawn procedurally (Hoshi's own art, except Classic's tatami image).
/// When <see cref="Animate"/> is on it redraws at ~24 fps with very slow motion: twinkling stars and a drifting
/// nebula, dissolving ink clouds, or shifting raked sand. Everything is deterministic for a given time.
/// </summary>
public sealed class ThemeBackground : Control
{
    public static readonly StyledProperty<BackgroundKind> KindProperty =
        AvaloniaProperty.Register<ThemeBackground, BackgroundKind>(nameof(Kind), BackgroundKind.NightSky);

    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<ThemeBackground, bool>(nameof(Animate), true);

    /// <summary>Seconds shown when not animating (a pleasant frame, and stable for screenshots).</summary>
    public const double StillTime = 12;

    private static readonly Star[] Stars = CreateStars();
    private static readonly Blob[] InkBlobs = CreateInkBlobs();
    private static readonly Fibre[] Fibres = CreateFibres();
    private static readonly Lazy<Bitmap?> Tatami = new(() => LoadAsset("avares://Hoshi/Assets/Sabaki/tatami.png"));
    private static readonly Lazy<Bitmap> PaperGrain = new(() => Noise(256, 0xEF, 0xE9, 0xDD, 7));
    private static readonly Lazy<Bitmap> InkGrain = new(() => Noise(256, 0x14, 0x15, 0x18, 5));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _timer;

    static ThemeBackground()
    {
        AffectsRender<ThemeBackground>(KindProperty, AnimateProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<ThemeBackground>(false);
    }

    public BackgroundKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool Animate
    {
        get => GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    /// <summary>Whether the timer is running (for tests).</summary>
    public bool IsRunning => _timer?.IsEnabled == true;

    public override void Render(DrawingContext context)
    {
        var size = new Rect(Bounds.Size);
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }

        double t = Animate ? StillTime + _clock.Elapsed.TotalSeconds : StillTime;
        using (context.PushClip(size))
        {
            switch (Kind)
            {
                case BackgroundKind.Tatami:
                    DrawTatami(context, size);
                    break;
                case BackgroundKind.NightSky:
                    DrawNightSky(context, size, t);
                    break;
                case BackgroundKind.InkMist:
                    DrawInkMist(context, size, t);
                    break;
                case BackgroundKind.ZenSand:
                    DrawZenSand(context, size, t);
                    break;
                case BackgroundKind.Washi:
                    DrawWashi(context, size);
                    break;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty || change.Property == AnimateProperty)
        {
            UpdateTimer();
        }
    }

    private void UpdateTimer()
    {
        bool moving = Animate
            && (Kind is BackgroundKind.NightSky or BackgroundKind.InkMist or BackgroundKind.ZenSand)
            && VisualRoot is not null;
        if (moving)
        {
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(42), DispatcherPriority.Render, (_, _) => InvalidateVisual());
            _timer.Start();
        }
        else
        {
            _timer?.Stop();
        }
    }

    // ---------------- Tatami (Classic) ----------------

    private static void DrawTatami(DrawingContext context, Rect r)
    {
        context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#C2CB9C")), r);
        if (Tatami.Value is not { } img)
        {
            return;
        }

        Tile(context, img, r);
    }

    // ---------------- Night sky ----------------

    private static readonly IBrush SkyGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#070B18"), 0), new GradientStop(Color.Parse("#111834"), 0.6), new GradientStop(Color.Parse("#1A2143"), 1) },
    }.ToImmutable();

    private static void DrawNightSky(DrawingContext context, Rect r, double t)
    {
        context.FillRectangle(SkyGradient, r);
        double m = Math.Max(r.Width, r.Height);

        // Nebula: three soft clouds drifting on slow Lissajous paths.
        Glow(context, r.Width * (0.22 + (0.05 * Math.Sin(t / 47))), r.Height * (0.28 + (0.05 * Math.Cos(t / 53))), m * 0.55, Color.FromArgb(0x46, 0x3C, 0x2E, 0x78));
        Glow(context, r.Width * (0.78 + (0.04 * Math.Cos(t / 61))), r.Height * (0.7 + (0.05 * Math.Sin(t / 41))), m * 0.5, Color.FromArgb(0x38, 0x1C, 0x4C, 0x80));
        Glow(context, r.Width * (0.55 + (0.06 * Math.Sin(t / 73))), r.Height * (0.45 + (0.04 * Math.Sin(t / 67))), m * 0.35, Color.FromArgb(0x16, 0xD8, 0xB4, 0x6A));

        // Constellations: faint gold lines between a few bright stars.
        var line = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x1C, 0xD8, 0xB4, 0x6A)), 0.8);
        foreach (int[] c in Constellations)
        {
            for (int i = 1; i < c.Length; i++)
            {
                context.DrawLine(line, Stars[c[i - 1]].At(r), Stars[c[i]].At(r));
            }
        }

        foreach (Star s in Stars)
        {
            double twinkle = 0.62 + (0.38 * Math.Sin((t * s.Speed) + s.Phase));
            byte a = (byte)Math.Clamp(s.Alpha * twinkle * 255, 0, 255);
            AvPoint p = s.At(r);
            if (s.Radius > 1.3)
            {
                Glow(context, p.X, p.Y, s.Radius * 7, Color.FromArgb((byte)(a / 5), s.Color.R, s.Color.G, s.Color.B));
            }

            context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(a, s.Color.R, s.Color.G, s.Color.B)), null, p, s.Radius, s.Radius);
        }

        // A rare shooting star: every 23 s, for 1.4 s.
        const double period = 23;
        double phase = t % period;
        if (phase < 1.4)
        {
            int n = (int)(t / period);
            double k = phase / 1.4;
            double ease = 1 - Math.Pow(1 - k, 3);
            double sx = r.Width * (0.15 + (0.6 * Frac(n * 0.618)));
            double sy = r.Height * (0.05 + (0.25 * Frac(n * 0.381)));
            var head = new AvPoint(sx + (ease * m * 0.28), sy + (ease * m * 0.12));
            var tail = new AvPoint(head.X - (m * 0.09), head.Y - (m * 0.04));
            byte alpha = (byte)(255 * Math.Sin(k * Math.PI));
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(tail, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(head, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Color.FromArgb(0, 255, 244, 220), 0), new GradientStop(Color.FromArgb(alpha, 255, 244, 220), 1) },
            };
            context.DrawLine(new Pen(brush, 1.4, lineCap: PenLineCap.Round), tail, head);
        }
    }

    private static readonly int[][] Constellations = [[3, 17, 29, 41, 52], [60, 71, 88, 95], [110, 123, 131, 140, 152]];

    // ---------------- Ink mist ----------------

    private static void DrawInkMist(DrawingContext context, Rect r, double t)
    {
        var paper = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#1C1D21"), 0), new GradientStop(Color.Parse("#0E0F11"), 1) },
        };
        context.FillRectangle(paper, r);
        double m = Math.Max(r.Width, r.Height);
        foreach (Blob b in InkBlobs)
        {
            double x = r.Width * (b.X + (b.Ax * Math.Sin((t / b.Px) + b.Phase)));
            double y = r.Height * (b.Y + (b.Ay * Math.Cos((t / b.Py) + b.Phase)));
            double breathe = 1 + (0.2 * Math.Sin((t / (b.Px * 0.7)) + (b.Phase * 2)));
            Glow(context, x, y, m * b.Radius * breathe, b.Color);
        }

        DrawEnso(context, r, t);
        using (context.PushOpacity(0.5))
        {
            Tile(context, InkGrain.Value, r);
        }
    }

    /// <summary>
    /// An ensō (the zen circle) painted in one stroke: thick where the brush lands, dry and broken where it lifts.
    /// Faint gold, slowly breathing.
    /// </summary>
    private static void DrawEnso(DrawingContext context, Rect r, double t)
    {
        double min = Math.Min(r.Width, r.Height);
        // Centred behind the goban, a little larger than it: in wide windows it frames the board on both sides.
        var c = new AvPoint(r.Width / 2, r.Height / 2);
        double radius = min * 0.6;
        double width = min * 0.06;
        double start = -Math.PI * 0.62;
        double sweep = Math.PI * 1.86;
        byte alpha = (byte)(0x1A + (0x08 * Math.Sin(t / 9)));
        var brush = new ImmutableSolidColorBrush(Color.FromArgb(alpha, 0xC9, 0xA4, 0x5C));
        const int bristles = 16;
        const int steps = 180;
        for (int b = 0; b < bristles; b++)
        {
            // Bristle position across the brush (-0.5 … 0.5). Side bristles touch later and lift earlier: the taper.
            double o = (b - ((bristles - 1) / 2.0)) / (bristles - 1);
            double noise = Frac(Math.Sin(b * 91.7) * 43758.5453);
            double kStart = (Math.Abs(o) * 0.1) + (noise * 0.02);
            double kEnd = 1 - (Math.Abs(o) * 0.45) - (noise * 0.12);
            var pen = new Pen(brush, width / bristles * 1.9, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                bool open = false;
                for (int i = 0; i <= steps; i++)
                {
                    double k = i / (double)steps;
                    // The dry end of the stroke: bristles skip in short runs.
                    double dry = Frac(Math.Sin((i / 6 * 12.9898) + (b * 78.233)) * 43758.5453);
                    bool ink = k >= kStart && k <= kEnd && !(k > 0.6 && dry < (k - 0.6) * 2.2);
                    if (!ink)
                    {
                        if (open)
                        {
                            ctx.EndFigure(false);
                            open = false;
                        }

                        continue;
                    }

                    double a = start + (sweep * k);
                    double rr = radius + (o * width) + (width * 0.12 * Math.Sin((k * 7) + 1));
                    var p = new AvPoint(c.X + (Math.Cos(a) * rr), c.Y + (Math.Sin(a) * rr));
                    if (!open)
                    {
                        ctx.BeginFigure(p, false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(p);
                    }
                }

                if (open)
                {
                    ctx.EndFigure(false);
                }
            }

            context.DrawGeometry(null, pen, g);
        }
    }

    // ---------------- Zen sand ----------------

    private static void DrawZenSand(DrawingContext context, Rect r, double t)
    {
        var sand = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#D8D2C3"), 0), new GradientStop(Color.Parse("#C9C1AF"), 1) },
        };
        context.FillRectangle(sand, r);

        double min = Math.Min(r.Width, r.Height);
        (AvPoint Center, double Radius)[] rocks =
        [
            (new AvPoint(r.Width * 0.035, r.Height * 0.8), min * 0.05),
            (new AvPoint(r.Width * 0.97, r.Height * 0.2), min * 0.038),
        ];
        const double spacing = 13;
        const int rings = 6;
        var groove = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x7A, 0x9C, 0x93, 0x7E)), 1.3);
        var ridge = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x9A, 0xEE, 0xE9, 0xDC)), 1);

        // Parallel raked lines that part around the rocks' ripples.
        for (double y0 = -spacing; y0 < r.Height + spacing; y0 += spacing)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                bool open = false;
                for (double x = -10; x <= r.Width + 10; x += 12)
                {
                    double y = y0 + (2.6 * Math.Sin((x / 95) + (y0 / 70) + (t * 0.12)));
                    bool inside = rocks.Any(k => Distance(k.Center, x, y) < k.Radius + (rings * spacing) + 4);
                    if (inside)
                    {
                        if (open)
                        {
                            ctx.EndFigure(false);
                            open = false;
                        }

                        continue;
                    }

                    if (!open)
                    {
                        ctx.BeginFigure(new AvPoint(x, y), false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(new AvPoint(x, y));
                    }
                }

                if (open)
                {
                    ctx.EndFigure(false);
                }
            }

            context.DrawGeometry(null, groove, geometry);
            using (context.PushTransform(Matrix.CreateTranslation(0, 1.3)))
            {
                context.DrawGeometry(null, ridge, geometry);
            }
        }

        // Concentric ripples and the rocks themselves.
        foreach ((AvPoint c, double radius) in rocks)
        {
            for (int k = 1; k <= rings; k++)
            {
                double rr = radius + (k * spacing) + (0.8 * Math.Sin((t * 0.3) + k));
                context.DrawEllipse(null, groove, c, rr, rr * 0.97);
                context.DrawEllipse(null, ridge, new AvPoint(c.X, c.Y + 1.3), rr, rr * 0.97);
            }

            context.DrawRectangle(
                new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x30, 0x28, 0x1C)), null,
                new Rect(c.X - radius, c.Y - radius, 2 * radius, 2 * radius), radius, radius,
                new BoxShadows(new BoxShadow { OffsetY = radius * 0.25, Blur = radius * 0.6, Color = Color.FromArgb(0x70, 0x30, 0x28, 0x1C) }));
            var stone = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#77736A"), 0), new GradientStop(Color.Parse("#3A3732"), 1) },
            };
            context.DrawEllipse(stone, null, c, radius, radius * 0.9);
        }
    }

    // ---------------- Washi ----------------

    private static void DrawWashi(DrawingContext context, Rect r)
    {
        Tile(context, PaperGrain.Value, r);
        foreach (Fibre f in Fibres)
        {
            var pen = new Pen(new ImmutableSolidColorBrush(f.Color), f.Width, lineCap: PenLineCap.Round);
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new AvPoint(f.X0 * r.Width, f.Y0 * r.Height), false);
                ctx.QuadraticBezierTo(new AvPoint(f.Cx * r.Width, f.Cy * r.Height), new AvPoint(f.X1 * r.Width, f.Y1 * r.Height));
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, g);
        }
    }

    // ---------------- Helpers ----------------

    private static void Glow(DrawingContext context, double x, double y, double radius, Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        };
        context.DrawEllipse(brush, null, new AvPoint(x, y), radius, radius);
    }

    private static void Tile(DrawingContext context, Bitmap img, Rect r)
    {
        Size s = img.Size;
        for (double y = 0; y < r.Height; y += s.Height)
        {
            for (double x = 0; x < r.Width; x += s.Width)
            {
                context.DrawImage(img, new Rect(s), new Rect(x, y, s.Width, s.Height));
            }
        }
    }

    private static double Distance(AvPoint c, double x, double y) => Math.Sqrt(((c.X - x) * (c.X - x)) + ((c.Y - y) * (c.Y - y)));

    private static double Frac(double v) => v - Math.Floor(v);

    private static Bitmap? LoadAsset(string uri)
    {
        try
        {
            using Stream s = AssetLoader.Open(new Uri(uri));
            return new Bitmap(s);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }
    }

    private static Bitmap Noise(int n, byte r, byte g, byte b, int amplitude)
    {
        var rng = new Random(11);
        var pixels = new int[n * n];
        for (int i = 0; i < pixels.Length; i++)
        {
            int d = rng.Next(-amplitude, amplitude + 1);
            pixels[i] = unchecked((int)0xFF000000) | (Clamp(r + d) << 16) | (Clamp(g + d) << 8) | Clamp(b + d);
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

    private static int Clamp(int v) => Math.Clamp(v, 0, 255);

    private static Star[] CreateStars()
    {
        var rng = new Random(2026);
        var stars = new Star[240];
        for (int i = 0; i < stars.Length; i++)
        {
            double big = rng.NextDouble();
            Color c = rng.NextDouble() switch
            {
                < 0.12 => Color.Parse("#FFE7B8"),
                < 0.3 => Color.Parse("#CFE0FF"),
                _ => Colors.White,
            };
            stars[i] = new Star(
                rng.NextDouble(), rng.NextDouble(),
                big > 0.96 ? 1.7 : big > 0.8 ? 1.15 : 0.45 + (rng.NextDouble() * 0.55),
                0.35 + (rng.NextDouble() * 0.6),
                0.4 + (rng.NextDouble() * 1.6),
                rng.NextDouble() * Math.PI * 2,
                c);
        }

        return stars;
    }

    private static Blob[] CreateInkBlobs()
    {
        var rng = new Random(7);
        var blobs = new Blob[9];
        for (int i = 0; i < blobs.Length; i++)
        {
            Color c = i switch
            {
                0 => Color.FromArgb(0x1C, 0xC9, 0xA4, 0x5C), // a whisper of gold
                < 4 => Color.FromArgb(0xA0, 0x02, 0x02, 0x03), // deep ink
                _ => Color.FromArgb(0x60, 0x55, 0x58, 0x62), // grey wash
            };
            blobs[i] = new Blob(
                rng.NextDouble(), rng.NextDouble(), 0.05 + (rng.NextDouble() * 0.08), 0.05 + (rng.NextDouble() * 0.08),
                50 + (rng.NextDouble() * 70), 50 + (rng.NextDouble() * 70), rng.NextDouble() * 6,
                0.18 + (rng.NextDouble() * 0.3), c);
        }

        return blobs;
    }

    private static Fibre[] CreateFibres()
    {
        var rng = new Random(5);
        var fibres = new Fibre[420];
        for (int i = 0; i < fibres.Length; i++)
        {
            double x = rng.NextDouble();
            double y = rng.NextDouble();
            double len = 0.01 + (rng.NextDouble() * 0.05);
            double a = rng.NextDouble() * Math.PI;
            bool light = rng.NextDouble() < 0.6;
            fibres[i] = new Fibre(
                x, y,
                x + (Math.Cos(a) * len * 0.5) + ((rng.NextDouble() - 0.5) * 0.01), y + (Math.Sin(a) * len * 0.5) + ((rng.NextDouble() - 0.5) * 0.01),
                x + (Math.Cos(a) * len), y + (Math.Sin(a) * len),
                0.5 + (rng.NextDouble() * 0.9),
                light ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFA) : Color.FromArgb(0x30, 0xB8, 0xAC, 0x98));
        }

        return fibres;
    }

    private readonly record struct Star(double X, double Y, double Radius, double Alpha, double Speed, double Phase, Color Color)
    {
        public AvPoint At(Rect r) => new(X * r.Width, Y * r.Height);
    }

    private readonly record struct Blob(double X, double Y, double Ax, double Ay, double Px, double Py, double Phase, double Radius, Color Color);

    private readonly record struct Fibre(double X0, double Y0, double Cx, double Cy, double X1, double Y1, double Width, Color Color);
}
