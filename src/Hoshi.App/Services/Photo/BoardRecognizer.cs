using Hoshi.Core;

namespace Hoshi.App.Services.Photo;

/// <summary>A photo as 8-bit BGRA pixels (what Avalonia and Skia hand over).</summary>
public sealed record PhotoPixels(int Width, int Height, byte[] Bgra)
{
    /// <summary>Luminance 0–255 at a pixel (outside: -1).</summary>
    public double Luma(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return -1;
        }

        int i = ((y * Width) + x) * 4;
        return (0.114 * Bgra[i]) + (0.587 * Bgra[i + 1]) + (0.299 * Bgra[i + 2]);
    }
}

/// <summary>What the photo showed: the stones, and how sure each point is (0–1).</summary>
public sealed record RecognizedBoard(int Size, Stone[,] Stones, double[,] Confidence)
{
    public int Count(Stone color) => Stones.Cast<Stone>().Count(s => s == color);
}

/// <summary>
/// "Photo of the board → SGF": the player marks the four corner intersections of the grid on the photo (top left,
/// top right, bottom right, bottom left, as seen); a homography maps every intersection into the photo even when it
/// was taken at an angle, and the brightness around each one sorts it into black stone, empty board or white stone
/// (three groups found by 1-D k-means, so lighting and wood colour do not matter). No machine learning and nothing
/// sent anywhere; the result opens in the editor for the player to correct.
/// </summary>
public static class BoardRecognizer
{
    /// <summary>Two groups closer than this (in luminance) are one: e.g. no white stones on the board.</summary>
    public const double MinSeparation = 28;

    public static RecognizedBoard Recognize(PhotoPixels photo, IReadOnlyList<(double X, double Y)> corners, int size)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Count != 4)
        {
            throw new ArgumentException("Four corners are needed.", nameof(corners));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(size, 5);
        double[] h = Homography(corners);
        double spacing = Math.Min(Distance(corners[0], corners[1]), Distance(corners[0], corners[3])) / (size - 1);
        int radius = Math.Max(1, (int)(spacing * 0.28));

        var luma = new double[size, size];
        var all = new List<double>(size * size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                (double px, double py) = Map(h, (double)x / (size - 1), (double)y / (size - 1));
                luma[x, y] = Sample(photo, px, py, radius);
                all.Add(luma[x, y]);
            }
        }

        (double dark, double board, double light) = Centers(all);
        bool hasBlack = board - dark >= MinSeparation;
        bool hasWhite = light - board >= MinSeparation;
        var stones = new Stone[size, size];
        var confidence = new double[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double v = luma[x, y];
                (Stone s, double d, double next) = Nearest(v, dark, board, light, hasBlack, hasWhite);
                stones[x, y] = s;
                confidence[x, y] = next <= 0 ? 1 : Math.Clamp(1 - (d / next), 0, 1);
            }
        }

        return new RecognizedBoard(size, stones, confidence);
    }

    /// <summary>The recognized position as a new game: setup stones, ready for the editor.</summary>
    public static Hoshi.Sgf.GameTree ToGame(RecognizedBoard board, Stone toPlay)
    {
        ArgumentNullException.ThrowIfNull(board);
        Hoshi.Sgf.GameTree tree = Hoshi.Sgf.GameTree.Create(board.Size, RuleSet.Japanese);
        var black = new List<string>();
        var white = new List<string>();
        for (int y = 0; y < board.Size; y++)
        {
            for (int x = 0; x < board.Size; x++)
            {
                if (board.Stones[x, y] == Stone.Black)
                {
                    black.Add(new Point(x, y).ToSgf());
                }
                else if (board.Stones[x, y] == Stone.White)
                {
                    white.Add(new Point(x, y).ToSgf());
                }
            }
        }

        if (black.Count > 0)
        {
            tree.Root.SetValues("AB", black);
        }

        if (white.Count > 0)
        {
            tree.Root.SetValues("AW", white);
        }

        tree.Root.SetValues("PL", [toPlay == Stone.White ? "W" : "B"]);
        return tree;
    }

    private static (Stone Stone, double Distance, double NextDistance) Nearest(double v, double dark, double board, double light, bool hasBlack, bool hasWhite)
    {
        var options = new List<(Stone, double)> { (Stone.Empty, Math.Abs(v - board)) };
        if (hasBlack)
        {
            options.Add((Stone.Black, Math.Abs(v - dark)));
        }

        if (hasWhite)
        {
            options.Add((Stone.White, Math.Abs(v - light)));
        }

        options.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return (options[0].Item1, options[0].Item2, options.Count > 1 ? options[1].Item2 : 0);
    }

    /// <summary>Three luminance groups (k-means from min, median, max).</summary>
    internal static (double Dark, double Board, double Light) Centers(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        double c0 = sorted[0], c1 = sorted[sorted.Length / 2], c2 = sorted[^1];
        for (int iteration = 0; iteration < 30; iteration++)
        {
            double s0 = 0, s1 = 0, s2 = 0;
            int n0 = 0, n1 = 0, n2 = 0;
            foreach (double v in sorted)
            {
                double d0 = Math.Abs(v - c0), d1 = Math.Abs(v - c1), d2 = Math.Abs(v - c2);
                if (d0 <= d1 && d0 <= d2)
                {
                    s0 += v;
                    n0++;
                }
                else if (d1 <= d2)
                {
                    s1 += v;
                    n1++;
                }
                else
                {
                    s2 += v;
                    n2++;
                }
            }

            (double, double, double) next = (n0 > 0 ? s0 / n0 : c0, n1 > 0 ? s1 / n1 : c1, n2 > 0 ? s2 / n2 : c2);
            if (Math.Abs(next.Item1 - c0) + Math.Abs(next.Item2 - c1) + Math.Abs(next.Item3 - c2) < 0.01)
            {
                break;
            }

            (c0, c1, c2) = next;
        }

        // The board is the biggest group in practice; keep the order dark ≤ board ≤ light.
        return (c0, c1, c2);
    }

    private static double Sample(PhotoPixels photo, double cx, double cy, int radius)
    {
        double sum = 0;
        int n = 0;
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                // A ring, not the very centre: the grid lines cross there and darken empty points.
                int d2 = (dx * dx) + (dy * dy);
                if (d2 > r2 || d2 < r2 / 9)
                {
                    continue;
                }

                double v = photo.Luma((int)Math.Round(cx + dx), (int)Math.Round(cy + dy));
                if (v >= 0)
                {
                    sum += v;
                    n++;
                }
            }
        }

        return n > 0 ? sum / n : 0;
    }

    /// <summary>The projective map from the unit square (0,0)-(1,1) onto the four corners (TL, TR, BR, BL).</summary>
    internal static double[] Homography(IReadOnlyList<(double X, double Y)> c)
    {
        // Solve for h (8 unknowns, h33 = 1) from the four correspondences.
        (double u, double v)[] src = [(0, 0), (1, 0), (1, 1), (0, 1)];
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            (double u, double v) = src[i];
            (double x, double y) = c[i];
            int r = i * 2;
            a[r, 0] = u; a[r, 1] = v; a[r, 2] = 1; a[r, 6] = -u * x; a[r, 7] = -v * x; a[r, 8] = x;
            a[r + 1, 3] = u; a[r + 1, 4] = v; a[r + 1, 5] = 1; a[r + 1, 6] = -u * y; a[r + 1, 7] = -v * y; a[r + 1, 8] = y;
        }

        // Gaussian elimination with partial pivoting.
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 8; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                throw new ArgumentException("The four corners must form a quadrilateral.");
            }

            for (int k = 0; k < 9; k++)
            {
                (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
            }

            for (int r = 0; r < 8; r++)
            {
                if (r == col)
                {
                    continue;
                }

                double f = a[r, col] / a[col, col];
                for (int k = col; k < 9; k++)
                {
                    a[r, k] -= f * a[col, k];
                }
            }
        }

        var h = new double[9];
        for (int i = 0; i < 8; i++)
        {
            h[i] = a[i, 8] / a[i, i];
        }

        h[8] = 1;
        return h;
    }

    internal static (double X, double Y) Map(double[] h, double u, double v)
    {
        double w = (h[6] * u) + (h[7] * v) + h[8];
        return (((h[0] * u) + (h[1] * v) + h[2]) / w, ((h[3] * u) + (h[4] * v) + h[5]) / w);
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
