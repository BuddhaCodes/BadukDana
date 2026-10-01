namespace Hoshi.Core;

public enum Corner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>A move in one corner's local sequence; a null point is a tenuki (the player played elsewhere).</summary>
public readonly record struct CornerMove(Stone Color, Point? Point)
{
    public bool IsTenuki => Point is null;
}

/// <summary>
/// Splits a game into the local sequences of its four corners, the way joseki are studied: each corner keeps its
/// own moves in order, and when one side plays twice in a row there, the other side's turn in between becomes a
/// tenuki (as in the OGS Joseki Explorer, where tenuki is recorded as a pass).
/// </summary>
public static class Corners
{
    /// <summary>
    /// A corner covers the 10×10 lines nearest to it (so a 19×19 corner reaches the side star points and a bit
    /// beyond), minus the far tip near the centre (tengen belongs to no corner).
    /// </summary>
    public const int Reach = 10;

    public static IReadOnlyList<Corner> All { get; } = [Corner.TopLeft, Corner.TopRight, Corner.BottomLeft, Corner.BottomRight];

    public static bool Contains(Corner corner, Point p, int size)
    {
        (int dx, int dy) = Distance(corner, p, size);
        return dx >= 0 && dy >= 0 && dx < Reach && dy < Reach && dx + dy <= (2 * Reach) - 4;
    }

    /// <summary>Distances (0-based) from the corner's two edges.</summary>
    public static (int Dx, int Dy) Distance(Corner corner, Point p, int size) => corner switch
    {
        Corner.TopLeft => (p.X, p.Y),
        Corner.TopRight => (size - 1 - p.X, p.Y),
        Corner.BottomLeft => (p.X, size - 1 - p.Y),
        _ => (size - 1 - p.X, size - 1 - p.Y),
    };

    /// <summary>The corner a point belongs to most (ties go to the top and to the left).</summary>
    public static Corner Nearest(Point p, int size)
    {
        bool top = p.Y <= (size - 1) / 2;
        bool left = p.X <= (size - 1) / 2;
        return (top, left) switch
        {
            (true, true) => Corner.TopLeft,
            (true, false) => Corner.TopRight,
            (false, true) => Corner.BottomLeft,
            _ => Corner.BottomRight,
        };
    }

    /// <summary>
    /// The local sequence of <paramref name="corner"/> from a game's moves (null point = pass, ignored). When the
    /// same colour plays twice in a row in the corner, a tenuki of the other colour is inserted between.
    /// </summary>
    public static IReadOnlyList<CornerMove> Sequence(IEnumerable<(Stone Color, Point? Point)> moves, Corner corner, int size)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var result = new List<CornerMove>();
        foreach ((Stone color, Point? point) in moves)
        {
            if (point is not { } p || !Contains(corner, p, size))
            {
                continue;
            }

            if (result.Count > 0 && result[^1].Color == color)
            {
                result.Add(new CornerMove(color.Opponent(), null));
            }

            result.Add(new CornerMove(color, p));
        }

        return result;
    }

    /// <summary>The two symmetries that carry <paramref name="from"/> onto <paramref name="to"/> (they differ by the diagonal mirror).</summary>
    public static IReadOnlyList<Symmetry> Mapping(Corner from, Corner to, int size)
    {
        Point probe = Anchor(from, size);
        Point target = Anchor(to, size);
        return [.. Symmetry.All.Where(s => s.Apply(probe, size) == target)];
    }

    private static Point Anchor(Corner corner, int size) => corner switch
    {
        Corner.TopLeft => new Point(0, 0),
        Corner.TopRight => new Point(size - 1, 0),
        Corner.BottomLeft => new Point(0, size - 1),
        _ => new Point(size - 1, size - 1),
    };
}
