using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// The "impact" drawn when a move is judged strong: a flash, shockwaves, flying embers, a board shake and, for
/// excellent and best moves, a damaged board (crater, fractures, chips) that fades away. Strength 1 = good, 2 = excellent, 3 = the best move.
/// Everything is precomputed from a seed, so a frame is a pure function of the elapsed time.
/// </summary>
internal sealed class ImpactEffect
{
    private const double GrowSeconds = 0.2;

    private readonly List<Fracture> _fractures = [];
    private readonly List<AvPoint> _crater = [];
    private readonly List<(AvPoint A, AvPoint B)> _shards = [];
    private readonly List<Chip> _chips = [];
    private readonly List<Ember> _embers = [];
    private double _craterRadius;

    /// <param name="subtle">Subtle effects: whatever the strength, only the small flash and one thin ring (no shake, damage or embers).</param>
    public ImpactEffect(Hoshi.Core.Point point, int strength, int seed, bool subtle = false)
    {
        Point = point;
        Strength = subtle ? 1 : Math.Clamp(strength, 1, 3);
        var rng = new Random(seed);
        Duration = Strength switch { 3 => 3.2, 2 => 2.0, _ => 0.9 };

        if (Strength >= 2)
        {
            BuildDamage(rng);

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

    /// <summary>
    /// The damaged board, below the stones: a shallow crater with a shattered floor, jagged fractures that are wide
    /// at the crater and taper to hairlines, ring fractures joining them (the best move) and a few chips of wood
    /// thrown out. Lit from the top left: every fracture has a dark occlusion halo and a light lip on its lower edge.
    /// For the best move the fractures first glow like molten metal, then cool to dark splits and fade.
    /// </summary>
    public void DrawCracks(DrawingContext context, AvPoint centre, double cell, double t)
    {
        if (_fractures.Count == 0)
        {
            return;
        }

        double fade = Math.Clamp((Duration - t) / (Duration * 0.4), 0, 1);
        if (fade <= 0)
        {
            return;
        }

        double grow = 1 - Math.Pow(1 - Math.Clamp(t / GrowSeconds, 0, 1), 3);
        double glow = Strength == 3 ? Math.Clamp(1 - (t / 1.3), 0, 1) : Math.Clamp(1 - (t / 0.35), 0, 1) * 0.6;
        double px = Math.Max(0.8, cell * 0.035); // one "pixel" of relief at this zoom

        // A broad soot/pressure stain around the crater.
        double stain = cell * (Strength == 3 ? 2.4 : 1.4);
        context.DrawEllipse(
            new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(110 * fade), 30, 14, 4), 0),
                    new GradientStop(Color.FromArgb((byte)(55 * fade), 45, 22, 8), 0.45),
                    new GradientStop(Color.FromArgb(0, 45, 22, 8), 1),
                },
            },
            null,
            centre,
            stain,
            stain);

        DrawCrater(context, centre, cell, fade, glow, px);

        byte ao = (byte)(60 * fade);
        byte lip = (byte)(120 * fade);
        byte dark = (byte)(235 * fade);
        foreach (Fracture f in _fractures)
        {
            double k = Math.Clamp((grow - f.Delay) / (1 - f.Delay), 0, 1);
            if (k <= 0)
            {
                continue;
            }

            // Occlusion halo, light lip (the same shape nudged down-right, under the dark split), the split itself.
            Fill(context, f, k, 2.6, centre, cell, default, Color.FromArgb(ao, 25, 12, 4));
            if (glow > 0)
            {
                Fill(context, f, k, 3.4, centre, cell, default, Color.FromArgb((byte)(110 * glow * fade), 255, 120, 20));
            }

            Fill(context, f, k, 1.0, centre, cell, new AvPoint(px, px), Color.FromArgb(lip, 255, 238, 205));
            // While hot the split is filled with molten light; it darkens as it cools.
            Fill(context, f, k, 1.0, centre, cell, default, Color.FromArgb((byte)(dark * (1 - (0.85 * glow))), 22, 10, 4));
            if (glow > 0)
            {
                Fill(context, f, k, 0.95, centre, cell, default, Color.FromArgb((byte)(255 * glow * fade), 255, (byte)(96 + (110 * glow)), 16));
                Fill(context, f, k, 0.45, centre, cell, default, Color.FromArgb((byte)(255 * glow * glow * fade), 255, 246, 210));
            }
        }

        DrawChips(context, centre, cell, t, fade, px);
    }

    private void DrawCrater(DrawingContext context, AvPoint centre, double cell, double fade, double glow, double px)
    {
        if (_crater.Count < 3)
        {
            return;
        }

        StreamGeometry rim = Polygon(_crater, centre, cell, default);
        double r = cell * _craterRadius;

        // Floor: darker towards the middle, as if pressed in.
        var floor = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(190 * fade), 34, 16, 6), 0),
                new GradientStop(Color.FromArgb((byte)(140 * fade), 70, 38, 16), 0.75),
                new GradientStop(Color.FromArgb((byte)(90 * fade), 110, 66, 30), 1),
            },
        };
        context.DrawGeometry(floor, null, rim);
        if (glow > 0)
        {
            var hot = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(230 * glow * fade), 255, 200, 90), 0),
                    new GradientStop(Color.FromArgb((byte)(160 * glow * fade), 255, 110, 20), 0.6),
                    new GradientStop(Color.FromArgb(0, 255, 90, 10), 1),
                },
            };
            context.DrawGeometry(hot, null, rim);
        }

        // Raised outer lip, pushed up by the impact: lit on the top left, shadowed on the bottom right.
        var outer = _crater.Select(p => new AvPoint(p.X * 1.1, p.Y * 1.1)).ToList();
        var lipBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(170 * fade), 255, 238, 205), 0.15),
                new GradientStop(Color.FromArgb(0, 200, 160, 110), 0.5),
                new GradientStop(Color.FromArgb((byte)(170 * fade), 30, 14, 4), 0.9),
            },
        };
        context.DrawGeometry(null, new Pen(lipBrush, Math.Max(1.2, r * 0.13), lineJoin: PenLineJoin.Round), Polygon(outer, centre, cell, default));

        // Inner walls: the top-left wall is in shadow, the bottom-right wall catches the light.
        var walls = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(230 * fade), 18, 8, 2), 0.2),
                new GradientStop(Color.FromArgb((byte)(40 * fade), 120, 80, 40), 0.55),
                new GradientStop(Color.FromArgb((byte)(200 * fade), 255, 236, 200), 0.85),
            },
        };
        context.DrawGeometry(null, new Pen(walls, Math.Max(1.2, r * 0.16), lineJoin: PenLineJoin.Round), rim);

        // Shattered floor: short straight splits from the centre to the rim.
        var split = new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(200 * fade), 16, 8, 2)), Math.Max(0.8, px * 1.1), lineCap: PenLineCap.Round);
        var lit = new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(90 * fade), 255, 230, 190)), Math.Max(0.6, px * 0.8), lineCap: PenLineCap.Round);
        foreach ((AvPoint a, AvPoint b) in _shards)
        {
            AvPoint pa = new(centre.X + (a.X * cell), centre.Y + (a.Y * cell));
            AvPoint pb = new(centre.X + (b.X * cell), centre.Y + (b.Y * cell));
            context.DrawLine(lit, new AvPoint(pa.X + px, pa.Y + px), new AvPoint(pb.X + px, pb.Y + px));
            context.DrawLine(split, pa, pb);
        }
    }

    private void DrawChips(DrawingContext context, AvPoint centre, double cell, double t, double fade, double px)
    {
        double fly = 1 - Math.Exp(-t / 0.12);
        foreach (Chip c in _chips)
        {
            double d = c.Start + ((c.End - c.Start) * fly);
            var at = new AvPoint(centre.X + (Math.Cos(c.Angle) * d * cell), centre.Y + (Math.Sin(c.Angle) * d * cell));
            double spin = c.Spin * fly;
            var pts = c.Shape.Select(p => new AvPoint(
                (p.X * Math.Cos(spin)) - (p.Y * Math.Sin(spin)),
                (p.X * Math.Sin(spin)) + (p.Y * Math.Cos(spin)))).ToList();
            StreamGeometry shadow = Polygon(pts, new AvPoint(at.X + (px * 1.5), at.Y + (px * 1.5)), cell, default);
            StreamGeometry chip = Polygon(pts, at, cell, default);
            context.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb((byte)(120 * fade), 20, 10, 4)), null, shadow);
            context.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * fade), 196, 150, 96)), null, chip);
            context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(160 * fade), 250, 226, 180)), Math.Max(0.5, px * 0.6)), chip);
        }
    }

    /// <summary>A fracture as a filled, tapering ribbon, revealed up to fraction <paramref name="k"/> of its length.</summary>
    private static void Fill(DrawingContext context, Fracture f, double k, double widthScale, AvPoint centre, double cell, AvPoint offset, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        double shown = f.Length * k;
        var left = new List<AvPoint>();
        var right = new List<AvPoint>();
        double s = 0;
        for (int i = 0; i < f.Points.Count; i++)
        {
            AvPoint p = f.Points[i];
            if (i > 0)
            {
                AvPoint prev = f.Points[i - 1];
                double seg = Distance(prev, p);
                if (s + seg > shown)
                {
                    double u = (shown - s) / seg;
                    p = new AvPoint(prev.X + ((p.X - prev.X) * u), prev.Y + ((p.Y - prev.Y) * u));
                    s = shown;
                    AddSide(f, i, p, s, widthScale, left, right);
                    break;
                }

                s += seg;
            }

            AddSide(f, i, p, s, widthScale, left, right);
        }

        if (left.Count < 2)
        {
            return;
        }

        right.Reverse();
        left.AddRange(right);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(Scale(left[0]), true);
            for (int i = 1; i < left.Count; i++)
            {
                ctx.LineTo(Scale(left[i]));
            }

            ctx.EndFigure(true);
        }

        context.DrawGeometry(new ImmutableSolidColorBrush(color), null, geometry);

        AvPoint Scale(AvPoint p) => new(centre.X + offset.X + (p.X * cell), centre.Y + offset.Y + (p.Y * cell));
    }

    private static void AddSide(Fracture f, int i, AvPoint p, double s, double widthScale, List<AvPoint> left, List<AvPoint> right)
    {
        // Normal from the neighbouring points (so joints mitre smoothly), width tapering to a point at the tip.
        AvPoint a = f.Points[Math.Max(0, i - 1)];
        AvPoint b = f.Points[Math.Min(f.Points.Count - 1, i + 1)];
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len = Math.Max(1e-6, Math.Sqrt((dx * dx) + (dy * dy)));
        double taper = Math.Pow(Math.Max(0, 1 - (s / f.Length)), 0.85);
        double half = f.Width * widthScale * taper * 0.5;
        left.Add(new AvPoint(p.X - (dy / len * half), p.Y + (dx / len * half)));
        right.Add(new AvPoint(p.X + (dy / len * half), p.Y - (dx / len * half)));
    }

    private static StreamGeometry Polygon(IReadOnlyList<AvPoint> points, AvPoint centre, double cell, AvPoint offset)
    {
        var geometry = new StreamGeometry();
        using StreamGeometryContext ctx = geometry.Open();
        ctx.BeginFigure(new AvPoint(centre.X + offset.X + (points[0].X * cell), centre.Y + offset.Y + (points[0].Y * cell)), true);
        for (int i = 1; i < points.Count; i++)
        {
            ctx.LineTo(new AvPoint(centre.X + offset.X + (points[i].X * cell), centre.Y + offset.Y + (points[i].Y * cell)));
        }

        ctx.EndFigure(true);
        return geometry;
    }

    private static double Distance(AvPoint a, AvPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

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

    private void BuildDamage(Random rng)
    {
        bool best = Strength == 3;
        _craterRadius = best ? 1.2 : 0.75;

        // Crater: an irregular polygon around the stone.
        int corners = best ? 15 : 11;
        for (int i = 0; i < corners; i++)
        {
            double a = i * Math.PI * 2 / corners;
            double r = _craterRadius * (0.82 + (0.3 * rng.NextDouble()));
            _crater.Add(new AvPoint(Math.Cos(a) * r, Math.Sin(a) * r));
        }

        for (int i = 0, n = best ? 7 : 4; i < n; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double r0 = _craterRadius * 0.15 * rng.NextDouble();
            double r1 = _craterRadius * (0.7 + (0.25 * rng.NextDouble()));
            _shards.Add((new AvPoint(Math.Cos(a) * r0, Math.Sin(a) * r0), new AvPoint(Math.Cos(a + 0.15) * r1, Math.Sin(a + 0.15) * r1)));
        }

        // Radial fractures from the rim outwards.
        int radials = best ? 8 : 5;
        double reach = best ? 4.6 : 2.2;
        double width = best ? 0.2 : 0.12;
        double start = rng.NextDouble() * Math.PI * 2;
        var angles = new List<double>();
        for (int i = 0; i < radials; i++)
        {
            double angle = start + (i * Math.PI * 2 / radials) + ((rng.NextDouble() - 0.5) * 0.45);
            angles.Add(angle);
            var from = new AvPoint(Math.Cos(angle) * _craterRadius * 0.85, Math.Sin(angle) * _craterRadius * 0.85);
            AddFracture(rng, from, angle, reach * (0.6 + (0.4 * rng.NextDouble())), width * (0.75 + (0.35 * rng.NextDouble())), 0, depth: 0);
        }

        // Ring fractures joining neighbouring radials (the classic shattered-ground web).
        if (best)
        {
            angles.Sort();
            foreach (double radius in (double[])[1.8, 3.0])
            {
                for (int i = 0; i < angles.Count; i++)
                {
                    if (rng.NextDouble() > 0.6)
                    {
                        continue;
                    }

                    double a0 = angles[i];
                    double a1 = i + 1 < angles.Count ? angles[i + 1] : angles[0] + (Math.PI * 2);
                    var pts = new List<AvPoint>();
                    int steps = Math.Max(3, (int)((a1 - a0) * radius / 0.3));
                    for (int j = 0; j <= steps; j++)
                    {
                        double a = a0 + ((a1 - a0) * j / steps);
                        double r = radius * (1 + ((rng.NextDouble() - 0.5) * 0.12));
                        pts.Add(new AvPoint(Math.Cos(a) * r, Math.Sin(a) * r));
                    }

                    // Ring cracks run from one radial towards the next, starting once the radials have reached them.
                    _fractures.Add(new Fracture(pts, PathLength(pts), 0.06, Math.Min(0.9, (radius / reach) * 0.8)));
                }
            }
        }

        // Chips of wood thrown out of the crater.
        for (int i = 0, n = best ? 12 : 5; i < n; i++)
        {
            double size = 0.06 + (0.1 * rng.NextDouble());
            var shape = new List<AvPoint>();
            int sides = 3 + rng.Next(2);
            for (int j = 0; j < sides; j++)
            {
                double a = (j * Math.PI * 2 / sides) + ((rng.NextDouble() - 0.5) * 0.8);
                shape.Add(new AvPoint(Math.Cos(a) * size * (0.6 + (0.6 * rng.NextDouble())), Math.Sin(a) * size * (0.6 + (0.6 * rng.NextDouble()))));
            }

            double angle = rng.NextDouble() * Math.PI * 2;
            _chips.Add(new Chip(shape, angle, _craterRadius * 0.8, _craterRadius + 0.2 + (rng.NextDouble() * (best ? 1.4 : 0.7)), (rng.NextDouble() - 0.5) * 6));
        }
    }

    private void AddFracture(Random rng, AvPoint from, double heading, double length, double width, double delay, int depth)
    {
        // Straight-ish segments with sharp kinks: rock and hard wood split in facets, not in curves.
        var points = new List<AvPoint> { from };
        AvPoint at = from;
        double angle = heading;
        double travelled = 0;
        int zig = rng.NextDouble() < 0.5 ? -1 : 1;
        while (travelled < length)
        {
            double step = Math.Min(length - travelled, 0.28 + (0.3 * rng.NextDouble()));
            zig = rng.NextDouble() < 0.7 ? -zig : zig;
            angle = heading + (zig * (0.12 + (0.35 * rng.NextDouble()))) + ((angle - heading) * 0.2);
            at = new AvPoint(at.X + (Math.Cos(angle) * step), at.Y + (Math.Sin(angle) * step));
            travelled += step;
            points.Add(at);

            double left = length - travelled;
            if (depth < 2 && travelled > length * 0.25 && left > 0.7 && rng.NextDouble() < (depth == 0 ? 0.28 : 0.15))
            {
                double side = rng.NextDouble() < 0.5 ? -1 : 1;
                double taper = Math.Pow(1 - (travelled / length), 0.85);
                double rootDelay = delay + ((1 - delay) * (travelled / length) * 0.85);
                AddFracture(rng, at, heading + (side * (0.35 + (0.45 * rng.NextDouble()))), left * (0.4 + (0.3 * rng.NextDouble())), width * taper * 0.8, rootDelay, depth + 1);
            }
        }

        _fractures.Add(new Fracture(points, PathLength(points), width, Math.Min(delay, 0.95)));
    }

    private static double PathLength(List<AvPoint> points)
    {
        double sum = 0;
        for (int i = 1; i < points.Count; i++)
        {
            sum += Distance(points[i - 1], points[i]);
        }

        return Math.Max(1e-6, sum);
    }

    /// <summary>A fracture: polyline in cells from the stone centre, total length, width at its root, growth delay (0–1).</summary>
    private sealed record Fracture(List<AvPoint> Points, double Length, double Width, double Delay);

    private sealed record Chip(List<AvPoint> Shape, double Angle, double Start, double End, double Spin);

    private sealed record Ember(double Vx, double Vy, double Size, double Life);
}
