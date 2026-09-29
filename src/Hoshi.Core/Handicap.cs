namespace Hoshi.Core;

/// <summary>Traditional fixed handicap placement.</summary>
public static class Handicap
{
    /// <summary>
    /// Maximum fixed handicap: 9 on odd boards from 7×7, 4 on even boards from 8×8 (no centre point), 0 below 7×7.
    /// </summary>
    public static int MaxFixed(int size) => size < 7 ? 0 : size % 2 == 0 ? 4 : 9;

    /// <summary>
    /// Star points for a fixed handicap, in the traditional order: upper right, lower left, lower right, upper left,
    /// then sides and centre (5 and 7 add the centre; 6 and 8 add the sides).
    /// </summary>
    public static IReadOnlyList<Point> FixedPoints(int size, int stones)
    {
        int max = MaxFixed(size);
        if (stones < 2 || stones > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stones), stones, $"A fixed handicap on {size}×{size} must be between 2 and {max} stones.");
        }

        int edge = size >= 13 ? 3 : 2;
        int lo = edge;
        int hi = size - 1 - edge;
        int mid = size / 2;

        Point upperRight = new(hi, lo);
        Point lowerLeft = new(lo, hi);
        Point lowerRight = new(hi, hi);
        Point upperLeft = new(lo, lo);
        Point centre = new(mid, mid);
        Point left = new(lo, mid);
        Point right = new(hi, mid);
        Point top = new(mid, lo);
        Point bottom = new(mid, hi);

        return stones switch
        {
            2 => [upperRight, lowerLeft],
            3 => [upperRight, lowerLeft, lowerRight],
            4 => [upperRight, lowerLeft, lowerRight, upperLeft],
            5 => [upperRight, lowerLeft, lowerRight, upperLeft, centre],
            6 => [upperRight, lowerLeft, lowerRight, upperLeft, left, right],
            7 => [upperRight, lowerLeft, lowerRight, upperLeft, left, right, centre],
            8 => [upperRight, lowerLeft, lowerRight, upperLeft, left, right, top, bottom],
            _ => [upperRight, lowerLeft, lowerRight, upperLeft, left, right, top, bottom, centre],
        };
    }
}
