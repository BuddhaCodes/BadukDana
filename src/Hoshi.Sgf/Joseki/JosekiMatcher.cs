using Hoshi.Core;

namespace Hoshi.Sgf.Joseki;

/// <summary>Where a joseki starts: the corner point of its first stone.</summary>
public enum JosekiFamily
{
    Hoshi,
    Komoku,
    SanSan,
    Takamoku,
    Mokuhazushi,
    Other,
}

public static class JosekiFamilies
{
    public static IReadOnlyList<JosekiFamily> All { get; } =
        [JosekiFamily.Hoshi, JosekiFamily.Komoku, JosekiFamily.SanSan, JosekiFamily.Takamoku, JosekiFamily.Mokuhazushi, JosekiFamily.Other];

    /// <summary>4-4 hoshi, 3-4 komoku, 3-3 san-san, 4-5 takamoku, 3-5 mokuhazushi, by the nearest corner.</summary>
    public static JosekiFamily Of(Point first, int size)
    {
        (int dx, int dy) = Corners.Distance(Corners.Nearest(first, size), first, size);
        (int a, int b) = (Math.Min(dx, dy) + 1, Math.Max(dx, dy) + 1);
        return (a, b) switch
        {
            (4, 4) => JosekiFamily.Hoshi,
            (3, 4) => JosekiFamily.Komoku,
            (3, 3) => JosekiFamily.SanSan,
            (4, 5) => JosekiFamily.Takamoku,
            (3, 5) => JosekiFamily.Mokuhazushi,
            _ => JosekiFamily.Other,
        };
    }

    public static JosekiFamily Of(JosekiLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Of(line.Moves[0].Point, line.Size);
    }
}

/// <summary>Known continuations of a corner sequence in a local joseki library.</summary>
public static class JosekiMatcher
{
    /// <summary>
    /// The next moves (in the board's orientation) of every line that starts like <paramref name="sequence"/>
    /// under some symmetry. Colours only need to agree relative to the first move, so a line recorded with Black
    /// first also matches the same shape started by White. Sequences with a tenuki never match local lines.
    /// </summary>
    public static IReadOnlyList<Point> Continuations(IReadOnlyList<JosekiLine> lines, IReadOnlyList<CornerMove> sequence, int size)
    {
        var found = new List<Point>();
        foreach ((JosekiLine line, Symmetry s) in Matching(lines, sequence, size))
        {
            if (line.Moves.Count > sequence.Count && s.Apply(line.Moves[sequence.Count].Point, size) is var next && !found.Contains(next))
            {
                found.Add(next);
            }
        }

        return found;
    }

    /// <summary>Every line (with the symmetry that fits) that starts like <paramref name="sequence"/>, including lines it completes.</summary>
    public static IReadOnlyList<(JosekiLine Line, Symmetry Symmetry)> Matching(IReadOnlyList<JosekiLine> lines, IReadOnlyList<CornerMove> sequence, int size)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Count == 0 || sequence.Any(m => m.IsTenuki))
        {
            return [];
        }

        var found = new List<(JosekiLine, Symmetry)>();
        foreach (JosekiLine line in lines)
        {
            if (line.Size != size || line.Moves.Count < sequence.Count)
            {
                continue;
            }

            foreach (Symmetry s in Symmetry.All)
            {
                if (Matches(line, s, sequence, size))
                {
                    found.Add((line, s));
                }
            }
        }

        return found;
    }

    private static bool Matches(JosekiLine line, Symmetry s, IReadOnlyList<CornerMove> sequence, int size)
    {
        for (int i = 0; i < sequence.Count; i++)
        {
            bool sameAsFirst = line.Moves[i].Color == line.Moves[0].Color;
            bool seqSameAsFirst = sequence[i].Color == sequence[0].Color;
            if (sameAsFirst != seqSameAsFirst || s.Apply(line.Moves[i].Point, size) != sequence[i].Point)
            {
                return false;
            }
        }

        return true;
    }
}
