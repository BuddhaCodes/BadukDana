namespace Hoshi.Core;

/// <summary>How safe a group is, from settled to in serious danger.</summary>
public enum GroupHealth
{
    /// <summary>Safe; not reported.</summary>
    Strong,

    /// <summary>Not settled yet: the engine sees it as only probably alive.</summary>
    Unsettled,

    /// <summary>In trouble: contested by the engine, or two liberties in a fight.</summary>
    Weak,

    /// <summary>In serious danger: probably lost, or a single liberty left.</summary>
    Critical,
}

/// <summary>A group that is not safe, with its health and a score (its owner's ownership in [-1, 1], or liberties).</summary>
public sealed record GroupStatus(Stone Color, IReadOnlyList<Point> Stones, GroupHealth Health, double Score);

/// <summary>
/// Reads which groups are weak. With an engine's ownership map (from Black's view, row-major) a group is as healthy
/// as the average ownership of its stones for its owner; without one, only short liberties in contact with the
/// enemy count, so quiet stones are never flagged. "Unsettled" is only reported for groups engaged with the enemy
/// (an enemy stone within <see cref="EngagedDistance"/>), so opening stones on their own are left alone. A group
/// in atari is always at least weak.
/// </summary>
public static class GroupStrength
{
    public const double StrongAbove = 0.6;
    public const double UnsettledAbove = 0.2;
    public const double WeakAbove = -0.2;

    /// <summary>An enemy stone this close (Chebyshev distance) makes a group "engaged".</summary>
    public const int EngagedDistance = 2;

    public static IReadOnlyList<GroupStatus> Assess(BoardState board, IReadOnlyList<double>? ownership = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        bool useOwnership = ownership is not null && ownership.Count == board.Width * board.Height;
        var seen = new HashSet<Point>();
        var result = new List<GroupStatus>();
        foreach (Point p in board.AllPoints)
        {
            Stone color = board[p];
            if (color == Stone.Empty || seen.Contains(p))
            {
                continue;
            }

            IReadOnlyList<Point> group = board.GetGroup(p);
            seen.UnionWith(group);
            int liberties = board.CountLiberties(p);
            GroupHealth byLiberties = ByLiberties(board, group, color, liberties);
            GroupHealth health = byLiberties;
            double score = liberties;
            if (useOwnership)
            {
                double sign = color == Stone.Black ? 1 : -1;
                score = group.Average(q => ownership![(q.Y * board.Width) + q.X] * sign);
                GroupHealth byEngine = score > StrongAbove ? GroupHealth.Strong
                    : score > UnsettledAbove ? GroupHealth.Unsettled
                    : score > WeakAbove ? GroupHealth.Weak
                    : GroupHealth.Critical;
                // The engine knows better than counting liberties, but an atari never looks fully safe.
                health = liberties == 1 && byEngine < GroupHealth.Weak ? GroupHealth.Weak : byEngine;
                if (health == GroupHealth.Unsettled && !IsEngaged(board, group, color))
                {
                    health = GroupHealth.Strong;
                }
            }

            if (health != GroupHealth.Strong)
            {
                result.Add(new GroupStatus(color, group, health, score));
            }
        }

        return result;
    }

    private static bool IsEngaged(BoardState board, IReadOnlyList<Point> group, Stone color)
    {
        Stone enemy = color == Stone.Black ? Stone.White : Stone.Black;
        foreach (Point q in group)
        {
            for (int dy = -EngagedDistance; dy <= EngagedDistance; dy++)
            {
                for (int dx = -EngagedDistance; dx <= EngagedDistance; dx++)
                {
                    var n = new Point(q.X + dx, q.Y + dy);
                    if (board.IsOnBoard(n) && board[n] == enemy)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static GroupHealth ByLiberties(BoardState board, IReadOnlyList<Point> group, Stone color, int liberties)
    {
        if (liberties == 1)
        {
            return GroupHealth.Critical;
        }

        Stone enemy = color == Stone.Black ? Stone.White : Stone.Black;
        bool contact = group.Any(q => board.Neighbors(q).Any(n => board[n] == enemy));
        return liberties == 2 && contact ? GroupHealth.Weak : GroupHealth.Strong;
    }
}
