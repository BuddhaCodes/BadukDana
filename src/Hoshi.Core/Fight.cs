namespace Hoshi.Core;

/// <summary>How fierce the fight around the last move is, from the board alone (no engine).</summary>
/// <param name="Intensity">This move: contact with enemy stones, short liberties nearby, captures (0–6).</param>
/// <param name="Heat">The battle so far: builds while moves stay in the same area, cools on tenuki (0–5.3).</param>
/// <param name="Strength">Effect strength for this move: 0 none, 1 skirmish, 2 hard fight, 3 all-out battle.</param>
public sealed record FightReading(double Intensity, double Heat, int Strength);

/// <summary>
/// Measures battles on the board, for effects and music when no engine may be used (live OGS games) or none is
/// configured. It only looks at stones and liberties — what both players can see — never at who is right.
/// </summary>
public sealed class FightMeter
{
    public const double MaxHeat = 5.3;

    /// <summary>Moves within this distance (Chebyshev) of the fight keep it going.</summary>
    public const int FightRadius = 4;

    private double _heat;
    private Point? _centre;
    private BoardState? _last;

    public double Heat => _heat;

    public void Reset()
    {
        _heat = 0;
        _centre = null;
        _last = null;
    }

    /// <summary>Reads a move that turned <paramref name="before"/> into <paramref name="after"/>.</summary>
    public FightReading OnMove(BoardState before, BoardState after, Point move)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (_last is not null && !ReferenceEquals(_last, before) && !SameStones(_last, before))
        {
            Reset(); // another game, or a jump through this one
        }

        _last = after;
        int captured = before.AllPoints.Count(p => before[p] != Stone.Empty && after[p] == Stone.Empty);
        double intensity = Intensity(before, after, move, captured);

        bool sameFight = _centre is { } c && Distance(c, move) <= FightRadius;
        _heat *= sameFight ? 0.82 : 0.45; // staying in the fight keeps it hot; tenuki lets it cool
        _heat = Math.Min(MaxHeat, _heat + (intensity * 0.55));
        if (intensity >= 1)
        {
            _centre = move;
        }

        double score = intensity + (0.35 * _heat);
        int strength = score >= 5 ? 3 : score >= 3.5 ? 2 : score >= 2.3 ? 1 : 0;
        return new FightReading(intensity, _heat, strength);
    }

    /// <summary>Contact with enemy stones, groups short of liberties around the move, and captures.</summary>
    public static double Intensity(BoardState before, BoardState after, Point move, int captured)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Stone mover = after[move];
        if (mover == Stone.Empty)
        {
            return 0;
        }

        Stone enemy = mover == Stone.Black ? Stone.White : Stone.Black;
        int contact = before.Neighbors(move).Count(n => before[n] == enemy);

        double tension = 0;
        var seen = new HashSet<Point>();
        foreach (Point p in after.AllPoints)
        {
            if (after[p] == Stone.Empty || Distance(p, move) > 2 || seen.Contains(p))
            {
                continue;
            }

            IReadOnlyList<Point> group = after.GetGroup(p);
            seen.UnionWith(group);
            tension += after.CountLiberties(p) switch { 1 => 1.5, 2 => 0.7, 3 => 0.25, _ => 0 };
        }

        double captures = (1.2 * Math.Min(captured, 4)) + (captured >= 3 ? 1 : 0);
        return Math.Min(6, (0.6 * contact) + Math.Min(4, tension) + captures);
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static bool SameStones(BoardState a, BoardState b) =>
        a.Width == b.Width && a.Height == b.Height && a.AllPoints.All(p => a[p] == b[p]);
}
