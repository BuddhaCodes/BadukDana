namespace Hoshi.Core;

/// <summary>One of the eight symmetries of a square board (rotations and reflections).</summary>
public sealed class Symmetry
{
    private readonly bool _swap;
    private readonly bool _flipX;
    private readonly bool _flipY;

    private Symmetry(int index, bool swap, bool flipX, bool flipY)
    {
        Index = index;
        _swap = swap;
        _flipX = flipX;
        _flipY = flipY;
    }

    /// <summary>All eight; the identity is first.</summary>
    public static IReadOnlyList<Symmetry> All { get; } =
        (from swap in new[] { false, true }
         from fx in new[] { false, true }
         from fy in new[] { false, true }
         select (swap, fx, fy)).Select((t, i) => new Symmetry(i, t.swap, t.fx, t.fy)).ToArray();

    public int Index { get; }

    public Symmetry Inverse => All.First(s => s.Apply(Apply(new Point(1, 2), 19), 19) == new Point(1, 2));

    public Point Apply(Point p, int size)
    {
        int x = _flipX ? size - 1 - p.X : p.X;
        int y = _flipY ? size - 1 - p.Y : p.Y;
        return _swap ? new Point(y, x) : new Point(x, y);
    }
}
