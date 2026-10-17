using System.Globalization;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Problems;

namespace Hoshi.App.Services.Tsumego;

/// <summary>
/// Problems from the player's games: each move KataGo judged a mistake or a blunder (with a better move it is sure
/// of) becomes "find the best move" in the position before it; the move played is the wrong answer.
/// </summary>
public static class MistakeProblems
{
    /// <summary>Only moves losing at least this many points become problems (smaller ones are hard to tell apart).</summary>
    public const double MinPointsLost = 3;

    public static IReadOnlyList<GameTree> Build(GameTree game, IReadOnlyDictionary<GameNode, StudyVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(verdicts);
        var cursor = new GameCursor(game);
        int size = cursor.BoardSize;
        string players = $"{game.Info.BlackPlayer ?? Tr.T("Common.Black")}–{game.Info.WhitePlayer ?? Tr.T("Common.White")}";
        var problems = new List<(int Move, GameTree Tree)>();
        foreach ((GameNode node, StudyVerdict v) in verdicts)
        {
            if (v.Quality is not (MoveQuality.Mistake or MoveQuality.Blunder) || v.PointsLost < MinPointsLost
                || v.Best is not { } best || node.Parent is not { } parent || node.GetMove(size) is not { Point: { } played } move || played == best)
            {
                continue;
            }

            BoardState before = cursor.GetBoard(parent);
            if (!before.IsLegal(move.Color, best))
            {
                continue;
            }

            int number = GameCursor.Path(node).Count(n => n.HasMove);
            string name = $"{players}, {Tr.F("Tsumego.FromGameMove", number)}";
            string wrong = Tr.F("Tsumego.FromGameWrong", played.ToHuman(before.Height), v.PointsLost.ToString("0.0", CultureInfo.CurrentCulture), best.ToHuman(before.Height));
            problems.Add((number, TsumegoWriter.BestMove(before, move.Color, best, played, name, "Tsumego.FindBest", wrong)));
        }

        return [.. problems.OrderBy(p => p.Move).Select(p => p.Tree)];
    }
}
