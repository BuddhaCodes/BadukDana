using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.Core;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// Captured stones shatter: each one cracks with a quick glint, splits into wedge-shaped shards that spin away
/// from the capturing stone and fade, leaving a puff of dust. Stones go in a wave outward from the capture, so a
/// big capture ripples across the group. A frame is a pure function of the elapsed time.
/// </summary>
internal sealed class CaptureEffect
{
    public const double ShardSeconds = 0.6;
    private const double WaveSecondsPerCell = 0.045;
    private const double MaxWave = 0.4;

    private readonly List<Victim> _victims = [];

    /// <param name="subtle">Subtle effects: the stones just shrink and fade where they were, without shards or glints.</param>
    public CaptureEffect(Point origin, IEnumerable<(Point Point, Stone Color)> captured, int seed, bool subtle = false)
    {
        IsSubtle = subtle;
        var rng = new Random(seed);
        foreach ((Point p, Stone color) in captured)
        {
            double dx = p.X - origin.X;
            double dy = p.Y - origin.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            double away = distance > 0 ? Math.Atan2(dy, dx) : rng.NextDouble() * Math.PI * 2;
            int count = 5 + rng.Next(2);
            double start = rng.NextDouble() * Math.PI * 2;
            var shards = new List<Shard>();
            for (int i = 0; i < count; i++)
            {
                double a0 = start + (i * Math.PI * 2 / count);
                double a1 = a0 + (Math.PI * 2 / count);
                double mid = (a0 + a1) / 2;
                // Fly outwards along the wedge, biased away from the capturing stone.
                double vx = (Math.Cos(mid) * 0.9) + (Math.Cos(away) * 0.6);
                double vy = (Math.Sin(mid) * 0.9) + (Math.Sin(away) * 0.6);
                shards.Add(new Shard(a0, a1, vx * (0.7 + (0.6 * rng.NextDouble())), vy * (0.7 + (0.6 * rng.NextDouble())), (rng.NextDouble() - 0.5) * 7));
            }

            _victims.Add(new Victim(p, color, Math.Min(MaxWave, distance * WaveSecondsPerCell), shards));
        }

        Duration = _victims.Count == 0 ? 0 : _victims.Max(v => v.Delay) + ShardSeconds + 0.15;
    }

    public double Duration { get; }

    public bool IsSubtle { get; }

    public int Count => _victims.Count;

    public bool IsDone(double t) => t >= Duration;

    public void Draw(
        DrawingContext context,
        double t,
        double cell,
        Func<Point, AvPoint> centre,
        Action<DrawingContext, AvPoint, double, Stone, Point> drawStone,
        double stoneRadius)
    {
        double r = cell * stoneRadius;
        foreach (Victim v in _victims)
        {
            double local = t - v.Delay;
            AvPoint c = centre(v.Point);
            if (local < 0)
            {
                // Still waiting for the wave: the stone is shown where it was.
                drawStone(context, c, r, v.Color, v.Point);
                continue;
            }

            double k = local / ShardSeconds;
            if (k >= 1.25)
            {
                continue;
            }

            if (IsSubtle)
            {
                if (k < 1)
                {
                    double fade = 1 - k;
                    using (context.PushOpacity(fade * fade))
                    {
                        drawStone(context, c, r * (1 - (0.25 * k)), v.Color, v.Point);
                    }
                }

                continue;
            }

            // Dust puff where the stone was.
            double puff = Math.Clamp(k / 1.25, 0, 1);
            byte dustAlpha = (byte)(90 * (1 - puff));
            var dust = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(dustAlpha, 230, 220, 200), 0),
                    new GradientStop(Color.FromArgb(0, 230, 220, 200), 1),
                },
            };
            double dr = r * (0.8 + (1.2 * puff));
            context.DrawEllipse(dust, null, c, dr, dr);

            if (k < 1)
            {
                double ease = 1 - Math.Pow(1 - k, 2);
                byte alpha = (byte)(255 * Math.Clamp(1.2 - (1.2 * k), 0, 1));
                foreach (Shard s in v.Shards)
                {
                    var at = new AvPoint(c.X + (s.Vx * ease * cell), c.Y + (s.Vy * ease * cell));
                    double scale = 1 - (0.35 * k);
                    var wedge = Wedge(r, s.A0, s.A1);
                    Matrix m = Matrix.CreateScale(scale, scale) * Matrix.CreateRotation(s.Spin * ease) * Matrix.CreateTranslation(at.X, at.Y);
                    using (context.PushOpacity(alpha / 255.0))
                    using (context.PushTransform(m))
                    using (context.PushGeometryClip(wedge))
                    {
                        drawStone(context, default, r, v.Color, v.Point);
                    }
                }

                // A bright glint as the stone cracks.
                double glint = 1 - (k / 0.25);
                if (glint > 0)
                {
                    var pen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(220 * glint), 255, 248, 225)), Math.Max(1, cell * 0.05));
                    context.DrawEllipse(null, pen, c, r * (1 + (0.4 * k)), r * (1 + (0.4 * k)));
                }
            }
        }
    }

    /// <summary>A pie slice of the stone, centred on the origin.</summary>
    private static StreamGeometry Wedge(double r, double a0, double a1)
    {
        var g = new StreamGeometry();
        using StreamGeometryContext ctx = g.Open();
        ctx.BeginFigure(default, true);
        const int steps = 6;
        for (int i = 0; i <= steps; i++)
        {
            double a = a0 + ((a1 - a0) * i / steps);
            ctx.LineTo(new AvPoint(Math.Cos(a) * r * 1.02, Math.Sin(a) * r * 1.02));
        }

        ctx.EndFigure(true);
        return g;
    }

    private sealed record Shard(double A0, double A1, double Vx, double Vy, double Spin);

    private sealed record Victim(Point Point, Stone Color, double Delay, List<Shard> Shards);
}
