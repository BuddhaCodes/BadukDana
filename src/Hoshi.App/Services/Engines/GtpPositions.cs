using Hoshi.Core;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.Services.Engines;

/// <summary>Builds the position a GTP engine needs from the game tree.</summary>
public static class GtpPositions
{
    /// <summary>
    /// The position at <paramref name="node"/>: the root's setup stones plus the moves that led there. When the path
    /// has setup after the root (an edited position), the engine gets that position as stones, without history.
    /// </summary>
    public static GtpPosition At(GameCursor cursor, GameNode node)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(node);
        GameTree tree = cursor.Tree;
        IReadOnlyList<GameNode> path = GameCursor.Path(node);
        bool setupAfterRoot = path.Skip(1).Any(n => n.HasProperty("AB") || n.HasProperty("AW") || n.HasProperty("AE"));
        BoardState here = cursor.GetBoard(node);
        BoardState start = setupAfterRoot ? here : cursor.GetBoard(tree.Root);
        var moves = new List<EngineMove>();
        if (!setupAfterRoot)
        {
            foreach (GameNode n in path.Skip(1))
            {
                if (n.GetMove(cursor.BoardSize) is { } m)
                {
                    moves.Add(new EngineMove(m.Color, m.Point));
                }
            }
        }

        return new GtpPosition
        {
            Width = start.Width,
            Height = start.Height,
            Komi = tree.Info.Komi ?? 0,
            Rules = tree.Info.Rules ?? RuleSet.Japanese,
            InitialStones = [.. start.AllPoints.Where(p => start[p] != Stone.Empty).Select(p => (p, start[p]))],
            Moves = moves,
            ToMove = here.ToMove,
        };
    }
}
