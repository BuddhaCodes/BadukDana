namespace Hoshi.Core;

/// <summary>A group of stones with a single liberty.</summary>
public sealed record AtariGroup(Stone Color, IReadOnlyList<Point> Stones, Point Liberty);

/// <summary>Finds groups in atari, and the ones a move has just put there.</summary>
public static class Atari
{
    public static IReadOnlyList<AtariGroup> Groups(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var seen = new HashSet<Point>();
        var groups = new List<AtariGroup>();
        foreach (Point p in board.AllPoints)
        {
            if (board[p] == Stone.Empty || seen.Contains(p))
            {
                continue;
            }

            IReadOnlyList<Point> group = board.GetGroup(p);
            seen.UnionWith(group);
            IReadOnlyList<Point> liberties = board.GetLiberties(p);
            if (liberties.Count == 1)
            {
                groups.Add(new AtariGroup(board[p], group, liberties[0]));
            }
        }

        return groups;
    }

    /// <summary>Groups in atari after a move that had no stone in atari before it (so each is announced once).</summary>
    public static IReadOnlyList<AtariGroup> NewlyInAtari(BoardState before, BoardState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var already = new HashSet<Point>(Groups(before).SelectMany(g => g.Stones));
        return [.. Groups(after).Where(g => !g.Stones.Any(already.Contains))];
    }
}
