namespace Hoshi.Core;

/// <summary>The star points (hoshi) of a board.</summary>
public static class StarPoints
{
    /// <summary>Star points for the usual board sizes (none for sizes without a convention).</summary>
    public static IReadOnlyList<Point> For(int columns, int rows)
    {
        if (columns != rows || columns < 7)
        {
            return [];
        }

        int size = columns;
        int e = size >= 13 ? 3 : 2;
        int hi = size - 1 - e;
        int mid = size / 2;
        var points = new List<Point> { new(e, e), new(hi, e), new(e, hi), new(hi, hi) };
        if (size % 2 == 1)
        {
            points.Add(new Point(mid, mid));
            if (size >= 15)
            {
                points.AddRange([new(mid, e), new(mid, hi), new(e, mid), new(hi, mid)]);
            }
        }

        return points;
    }
}
