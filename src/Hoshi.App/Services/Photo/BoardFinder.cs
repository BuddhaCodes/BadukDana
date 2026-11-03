namespace Hoshi.App.Services.Photo;

/// <summary>
/// Finds the grid's four corner intersections in a photo by itself, so the player only checks them:
/// 1) the board is the largest wood-coloured region (warm hue, some saturation, not dark), found on a small copy;
/// 2) its four extreme points give the board's outline, even seen at an angle;
/// 3) the grid sits inside that outline with a margin: the margin that puts the most dark pixels on the predicted
///    lines (and the fewest between them) wins. Stones inside the board do not matter: the region's outline does.
/// Returns null when no board-like region is found (a white or very dark board, a cluttered table): the player then
/// taps the corners as before.
/// </summary>
public static class BoardFinder
{
    private const int Work = 240;

    public static IReadOnlyList<(double X, double Y)>? FindCorners(PhotoPixels photo, int size)
    {
        ArgumentNullException.ThrowIfNull(photo);
        double scale = Math.Min(1, (double)Work / Math.Max(photo.Width, photo.Height));
        int w = Math.Max(8, (int)(photo.Width * scale)), h = Math.Max(8, (int)(photo.Height * scale));
        var wood = new bool[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                wood[x, y] = IsWood(photo, (int)(x / scale), (int)(y / scale));
            }
        }

        // Stones and lines break the wood: close the gaps a little before looking for the region.
        wood = Close(wood, w, h, radius: Math.Max(2, w / 60));
        List<(int X, int Y)>? region = Largest(wood, w, h);
        if (region is null || region.Count < w * h / 20)
        {
            return null;
        }

        (int X, int Y) tl = region.MinBy(p => p.X + p.Y), br = region.MaxBy(p => p.X + p.Y);
        (int X, int Y) tr = region.MaxBy(p => p.X - p.Y), bl = region.MaxBy(p => p.Y - p.X);
        (double X, double Y)[] outline =
        [
            ((tl.X + 0.5) / scale, (tl.Y + 0.5) / scale),
            ((tr.X + 0.5) / scale, (tr.Y + 0.5) / scale),
            ((br.X + 0.5) / scale, (br.Y + 0.5) / scale),
            ((bl.X + 0.5) / scale, (bl.Y + 0.5) / scale),
        ];

        try
        {
            return Inset(photo, outline, size);
        }
        catch (ArgumentException)
        {
            return null; // the outline was degenerate
        }
    }

    /// <summary>The grid corners inside the board outline: the margin whose predicted lines are darkest.</summary>
    private static (double X, double Y)[] Inset(PhotoPixels photo, (double X, double Y)[] outline, int size)
    {
        double[] h = BoardRecognizer.Homography(outline);
        double best = double.MinValue, bestU = 0.04, bestV = 0.04;
        for (double mu = 0; mu <= 0.12; mu += 0.004)
        {
            for (double mv = 0; mv <= 0.12; mv += 0.004)
            {
                double score = LineScore(photo, h, mu, mv, size);
                if (score > best)
                {
                    (best, bestU, bestV) = (score, mu, mv);
                }
            }
        }

        // Then finer around the best, each side on its own (boards are not always cut evenly, and perspective shifts them).
        double left = bestU, right = bestU, top = bestV, bottom = bestV;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (int side in new[] { 0, 1, 2, 3 })
            {
                double keep = side switch { 0 => left, 1 => right, 2 => top, _ => bottom };
                double sideBest = double.MinValue, chosen = keep;
                for (double m = Math.Max(0, keep - 0.008); m <= keep + 0.008; m += 0.001)
                {
                    double l = side == 0 ? m : left, r = side == 1 ? m : right, t = side == 2 ? m : top, b = side == 3 ? m : bottom;
                    double score = LineScore(photo, h, l, r, t, b, size);
                    if (score > sideBest)
                    {
                        (sideBest, chosen) = (score, m);
                    }
                }

                switch (side)
                {
                    case 0: left = chosen; break;
                    case 1: right = chosen; break;
                    case 2: top = chosen; break;
                    default: bottom = chosen; break;
                }
            }
        }

        (double, double) At(double u, double v) => BoardRecognizer.Map(h, u, v);
        return Snap(photo, [At(left, top), At(1 - right, top), At(1 - right, 1 - bottom), At(left, 1 - bottom)], size);
    }

    /// <summary>
    /// Last, each grid corner on its own, in the photo: the outline's corners are only as good as the small copy, and a
    /// board seen at an angle bends the margins. Moves each corner in shrinking steps while the lines get darker.
    /// </summary>
    private static (double X, double Y)[] Snap(PhotoPixels photo, (double X, double Y)[] corners, int size)
    {
        (double X, double Y)[] c = [.. corners];
        double side = Math.Sqrt(Math.Pow(c[1].X - c[0].X, 2) + Math.Pow(c[1].Y - c[0].Y, 2));
        double spacing = side / (size - 1);
        var sums = new Integral(photo);
        int blur = 0;
        double Score() => LineScore((x, y) => sums.Mean(x, y, blur), BoardRecognizer.Homography(c), 0, 0, 0, 0, size);
        foreach (double fraction in new[] { 0.12, 0.06, 0.03, 0.015 })
        {
            // Thin lines give a spiky score: blur as wide as the step so a line a step away is still seen.
            double step = spacing * fraction;
            blur = Math.Max(1, (int)Math.Round(step));
            double best = Score();
            for (int round = 0; round < 3; round++)
            {
                bool moved = false;
                for (int i = 0; i < 4; i++)
                {
                    foreach ((double dx, double dy) in new[] { (step, 0.0), (-step, 0.0), (0.0, step), (0.0, -step) })
                    {
                        (double X, double Y) keep = c[i];
                        c[i] = (keep.X + dx, keep.Y + dy);
                        double score = Score();
                        if (score > best)
                        {
                            best = score;
                            moved = true;
                        }
                        else
                        {
                            c[i] = keep;
                        }
                    }
                }

                if (!moved)
                {
                    break;
                }
            }
        }

        return c;
    }

    /// <summary>Summed-area table of the luma: the mean of any square in four reads.</summary>
    private sealed class Integral
    {
        private readonly double[] _sum;
        private readonly int _w;
        private readonly int _h;

        public Integral(PhotoPixels photo)
        {
            (_w, _h) = (photo.Width, photo.Height);
            _sum = new double[(_w + 1) * (_h + 1)];
            for (int y = 0; y < _h; y++)
            {
                double row = 0;
                for (int x = 0; x < _w; x++)
                {
                    row += photo.Luma(x, y);
                    _sum[((y + 1) * (_w + 1)) + x + 1] = _sum[(y * (_w + 1)) + x + 1] + row;
                }
            }
        }

        public double Mean(double cx, double cy, int r)
        {
            int x0 = Math.Max(0, (int)cx - r), y0 = Math.Max(0, (int)cy - r);
            int x1 = Math.Min(_w, (int)cx + r + 1), y1 = Math.Min(_h, (int)cy + r + 1);
            if (x1 <= x0 || y1 <= y0)
            {
                return -1;
            }

            double total = _sum[(y1 * (_w + 1)) + x1] - _sum[(y0 * (_w + 1)) + x1] - _sum[(y1 * (_w + 1)) + x0] + _sum[(y0 * (_w + 1)) + x0];
            return total / ((x1 - x0) * (y1 - y0));
        }
    }

    private static double LineScore(PhotoPixels photo, double[] h, double mu, double mv, int size) =>
        LineScore(photo, h, mu, mu, mv, mv, size);

    // Darkness on the predicted grid lines minus darkness halfway between them (sampled sparsely: it is a search).
    private static double LineScore(PhotoPixels photo, double[] h, double left, double right, double top, double bottom, int size) =>
        LineScore((x, y) => photo.Luma((int)x, (int)y), h, left, right, top, bottom, size);

    private static double LineScore(Func<double, double, double> luma, double[] h, double left, double right, double top, double bottom, int size)
    {
        double on = 0, off = 0;
        int n = 0;
        for (int i = 0; i < size; i++)
        {
            for (int t = 1; t < 40; t++)
            {
                double s = t / 40.0;
                double line = (double)i / (size - 1), between = (i + 0.5) / (size - 1);
                foreach ((double a, double b, bool isLine) in new[] { (line, s, true), (s, line, true), (between, s, false), (s, between, false) })
                {
                    if (!isLine && i == size - 1)
                    {
                        continue;
                    }

                    double u = left + (a * (1 - left - right)), v = top + (b * (1 - top - bottom));
                    (double x, double y) = BoardRecognizer.Map(h, u, v);
                    double l = luma(x, y);
                    if (l < 0)
                    {
                        continue;
                    }

                    if (isLine)
                    {
                        on += 255 - l;
                        n++;
                    }
                    else
                    {
                        off += 255 - l;
                    }
                }
            }
        }

        return n == 0 ? double.MinValue : (on - off) / n;
    }

    /// <summary>Kaya, katsura, bamboo, spruce: warm hue, a little saturation, not dark. Black/white stones are not.</summary>
    internal static bool IsWood(PhotoPixels photo, int x, int y)
    {
        if (x < 0 || y < 0 || x >= photo.Width || y >= photo.Height)
        {
            return false;
        }

        int i = ((y * photo.Width) + x) * 4;
        double b = photo.Bgra[i] / 255.0, g = photo.Bgra[i + 1] / 255.0, r = photo.Bgra[i + 2] / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max < 0.35 || max - min < 0.12 * max)
        {
            return false; // too dark or too grey
        }

        double hue = max == r ? 60 * (((g - b) / (max - min)) % 6) : max == g ? 60 * (((b - r) / (max - min)) + 2) : 60 * (((r - g) / (max - min)) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        return hue is >= 15 and <= 65;
    }

    private static bool[,] Close(bool[,] mask, int w, int h, int radius)
    {
        bool[,] Grow(bool[,] m, bool value)
        {
            var result = new bool[w, h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool any = !value;
                    for (int dy = -radius; dy <= radius && any != value; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            bool v = xx >= 0 && yy >= 0 && xx < w && yy < h && m[xx, yy];
                            if (v == value)
                            {
                                any = value;
                                break;
                            }
                        }
                    }

                    result[x, y] = any;
                }
            }

            return result;
        }

        return Grow(Grow(mask, true), false); // dilate, then erode
    }

    private static List<(int X, int Y)>? Largest(bool[,] mask, int w, int h)
    {
        var seen = new bool[w, h];
        List<(int, int)>? best = null;
        var stack = new Stack<(int X, int Y)>();
        for (int y0 = 0; y0 < h; y0++)
        {
            for (int x0 = 0; x0 < w; x0++)
            {
                if (!mask[x0, y0] || seen[x0, y0])
                {
                    continue;
                }

                var region = new List<(int, int)>();
                stack.Push((x0, y0));
                seen[x0, y0] = true;
                while (stack.Count > 0)
                {
                    (int x, int y) = stack.Pop();
                    region.Add((x, y));
                    foreach ((int nx, int ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    {
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny] && !seen[nx, ny])
                        {
                            seen[nx, ny] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }

                if (best is null || region.Count > best.Count)
                {
                    best = region;
                }
            }
        }

        return best;
    }
}
