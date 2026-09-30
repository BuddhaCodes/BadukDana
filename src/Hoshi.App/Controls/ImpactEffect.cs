using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// The "impact" drawn when a move is judged strong: a flash, shockwaves, flying embers, a board shake and, for the
/// strongest moves, glowing cracks in the wood that fade away. Strength 1 = good, 2 = excellent, 3 = the best move.
/// Everything is precomputed from a seed, so a frame is a pure function of the elapsed time.
/// </summary>
internal sealed class ImpactEffect
{
    private readonly List<Crack> _cracks = [];
    private readonly List<Ember> _embers = [];

    public ImpactEffect(Hoshi.Core.Point point, int strength, int seed)
    {
        Point = point;
        Strength = Math.Clamp(strength, 1, 3);
        var rng = new Random(seed);
        Duration = Strength switch { 3 => 2.6, 2 => 1.8, _ => 0.9 };

        if (Strength >= 2)
        {
            int branches = Strength == 3 ? 9 : 5;
            double reach = Strength == 3 ? 4.8 : 2.4;
            double start = rng.NextDouble() * Math.PI * 2;
            for (int i = 0; i < branches; i++)
            {
                double angle = start + (i * Math.PI * 2 / branches) + ((rng.NextDouble() - 0.5) * 0.5);
                AddCrack(rng, new AvPoint(Math.Cos(angle) * 0.45, Math.Sin(angle) * 0.45), angle, reach * (0.55 + (0.45 * rng.NextDouble())), 0.12, depth: 0);
            }

            int embers = Strength == 3 ? 56 : 24;
            for (int i = 0; i < embers; i++)
            {
                double angle = rng.NextDouble() * Math.PI * 2;
                double speed = (Strength == 3 ? 13 : 8) * (0.3 + (0.7 * rng.NextDouble()));
                _embers.Add(new Ember(Math.Cos(angle) * speed, Math.Sin(angle) * speed, 0.05 + (0.06 * rng.NextDouble()), 0.45 + (0.8 * rng.NextDouble())));
            }
        }
    }

    public Hoshi.Core.Point Point { get; }

    public int Strength { get; }

    /// <summary>Seconds until the last trace (the cracks) has faded.</summary>
    public double Duration { get; }

    public bool IsDone(double t) => t >= Duration;

    /// <summary>Board shake in pixels: a fast, decaying jitter (none for strength 1).</summary>
    public AvPoint Shake(double t, double cell)
    {
        if (Strength < 2)
        {
            return default;
        }

        double amp = cell * (Strength == 3 ? 0.2 : 0.09) * Math.Exp(-t / (Strength == 3 ? 0.2 : 0.12));
        return amp < 0.2 ? default : new AvPoint(amp * Math.Sin(t * 2 * Math.PI * 23), amp * Math.Cos(t * 2 * Math.PI * 29) * 0.8);
    }

    /// <summary>Cracks in the wood, below the stones: they race out, glow like embers, then fade.</summary>
    public void DrawCracks(DrawingContext context, AvPoint centre, double cell, double t)
    {
        if (_cracks.Count == 0)
        {
            return;
        }

        double grow = Math.Clamp(t / 0.14, 0, 1);
        double fade = Math.Clamp((Duration - t) / (Duration * 0.45), 0, 1);
        double glow = Math.Clamp(1 - (t / 1.1), 0, 1);
        if (fade <= 0)
        {
            return;
        }

        if (Strength == 3)
        {
            // A scorch mark under the stone, cooling with the cracks.
            double r = cell * 1.6;
            var scorch = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(170 * fade), 40, 18, 6), 0),
                    new GradientStop(Color.FromArgb((byte)(120 * fade), 48, 22, 8), 0.42),
                    new GradientStop(Color.FromArgb((byte)(110 * fade * glow), 255, 110, 20), 0.6),
                    new GradientStop(Color.FromArgb(0, 40, 18, 6), 1),
                },
            };
            context.DrawEllipse(scorch, null, centre, r, r);
        }

        foreach (Crack c in _cracks)
        {
            // Sub-branches start when their parent has grown past their root.
            double k = Math.Clamp((grow - c.Delay) / (1 - c.Delay), 0, 1);
            if (k <= 0 || c.Points.Count < 2)
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(Scale(c.Points[0]), false);
                int n = Math.Clamp((int)Math.Ceiling(k * (c.Points.Count - 1)), 1, c.Points.Count - 1);
                for (int i = 1; i <= n; i++)
                {
                    ctx.LineTo(Scale(c.Points[i]));
                }

                ctx.EndFigure(false);
            }

            double width = Math.Max(0.8, cell * c.Width);
            if (glow > 0)
            {
                var hot = Color.FromArgb((byte)(200 * glow * fade), 255, (byte)(120 + (100 * glow)), 30);
                context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(hot), width * 3.2, lineCap: PenLineCap.Round), geometry);
            }

            context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(215 * fade), 28, 14, 6)), width, lineCap: PenLineCap.Round), geometry);
            if (glow > 0)
            {
                var core = Color.FromArgb((byte)(255 * glow * glow * fade), 255, 236, 180);
                context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(core), width * 0.45, lineCap: PenLineCap.Round), geometry);
            }
        }

        AvPoint Scale(AvPoint p) => new(centre.X + (p.X * cell), centre.Y + (p.Y * cell));
    }

    /// <summary>Flash, shockwaves and embers, above the stones.</summary>
    public void DrawBurst(DrawingContext context, AvPoint centre, double cell, double t)
    {
        // Flash: a hot white core that burns out in a fraction of a second.
        double flash = 1 - (t / (Strength == 3 ? 0.28 : 0.18));
        if (flash > 0)
        {
            double r = cell * (Strength == 3 ? 2.6 : Strength == 2 ? 1.6 : 1.0);
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(230 * flash), 255, 250, 235), 0),
                    new GradientStop(Color.FromArgb((byte)(140 * flash), 255, 190, 90), 0.35),
                    new GradientStop(Color.FromArgb(0, 255, 140, 40), 1),
                },
            };
            context.DrawEllipse(brush, null, centre, r, r);
        }

        // Shockwaves: rings racing outwards; the best move gets a second, delayed ring.
        double maxRadius = Strength switch { 3 => 7.5, 2 => 4.5, _ => 2.2 };
        int rings = Strength == 3 ? 2 : 1;
        for (int i = 0; i < rings; i++)
        {
            double k = Math.Clamp((t - (i * 0.12)) / (Strength == 1 ? 0.55 : 0.8), 0, 1);
            if (k is <= 0 or >= 1)
            {
                continue;
            }

            double ease = 1 - Math.Pow(1 - k, 3);
            double radius = cell * (0.5 + ((maxRadius - 0.5) * ease));
            byte alpha = (byte)((i == 0 ? 210 : 140) * (1 - k));
            double width = Math.Max(1, cell * (Strength == 1 ? 0.06 : 0.16) * (1 - (0.8 * k)));
            context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(alpha, 255, 226, 170)), width), centre, radius, radius);
            if (Strength >= 2)
            {
                context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(alpha / 3), 255, 160, 60)), width * 3), centre, radius * 0.97, radius * 0.97);
            }
        }

        // Embers: sparks thrown out with drag, cooling from white to orange to nothing.
        foreach (Ember e in _embers)
        {
            double life = t / e.Life;
            if (life >= 1)
            {
                continue;
            }

            // Distance with exponential drag: v0·τ·(1 − e^(−t/τ)).
            const double tau = 0.28;
            double travel = tau * (1 - Math.Exp(-t / tau));
            var p = new AvPoint(centre.X + (e.Vx * travel * cell), centre.Y + (e.Vy * travel * cell));
            byte a = (byte)(255 * (1 - life));
            var color = life < 0.3
                ? Color.FromArgb(a, 255, 245, 210)
                : Color.FromArgb(a, 255, (byte)(200 - (120 * life)), (byte)(80 - (60 * life)));
            double size = Math.Max(0.8, cell * e.Size * (1 - (0.5 * life)));
            // A short motion streak behind each spark while it is still fast.
            double back = tau * (1 - Math.Exp(-Math.Max(0, t - 0.035) / tau));
            var tail = new AvPoint(centre.X + (e.Vx * back * cell), centre.Y + (e.Vy * back * cell));
            var streak = Color.FromArgb((byte)(a * 0.6), color.R, color.G, color.B);
            context.DrawLine(new Pen(new ImmutableSolidColorBrush(streak), size * 1.2, lineCap: PenLineCap.Round), tail, p);
            context.DrawEllipse(new ImmutableSolidColorBrush(color), null, p, size, size);
        }
    }

    private void AddCrack(Random rng, AvPoint from, double angle, double length, double width, int depth, double delay = 0)
    {
        var points = new List<AvPoint> { from };
        double step = 0.22;
        AvPoint at = from;
        double travelled = 0;
        double heading = angle;
        while (travelled < length)
        {
            // Jagged like split wood, but keeping to its direction instead of wandering.
            angle += ((rng.NextDouble() - 0.5) * 0.8) + ((heading - angle) * 0.35);
            at = new AvPoint(at.X + (Math.Cos(angle) * step), at.Y + (Math.Sin(angle) * step));
            travelled += step;
            points.Add(at);

            if (depth < 2 && travelled > 0.5 && length - travelled > 0.6 && rng.NextDouble() < (depth == 0 ? 0.1 : 0.05))
            {
                double side = rng.NextDouble() < 0.5 ? -1 : 1;
                double rootDelay = delay + ((1 - delay) * (travelled / length) * 0.9);
                AddCrack(rng, at, angle + (side * (0.5 + (0.6 * rng.NextDouble()))), (length - travelled) * 0.55, width * 0.6, depth + 1, rootDelay);
            }
        }

        _cracks.Add(new Crack(points, width * (0.45 + (0.25 * rng.NextDouble())) * 0.85, Math.Min(delay, 0.95)));
    }

    private sealed record Crack(List<AvPoint> Points, double Width, double Delay);

    private sealed record Ember(double Vx, double Vy, double Size, double Life);
}
