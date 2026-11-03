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

    /// <summary>In the endgame a smaller gap is already worth practising: the biggest move by a point or more.</summary>
    public const double MinEndgamePointsLost = 1;

    /// <summary>Mistakes before the endgame (3 points or more): "find the best move".</summary>
    public static IReadOnlyList<GameTree> Build(GameTree game, IReadOnlyDictionary<GameNode, StudyVerdict> verdicts) =>
        Make(game, verdicts, endgame: false);

    /// <summary>
    /// Endgame moves (<see cref="GamePhases"/>) that lost a point or more: "find the biggest move" — the endgame
    /// trainer, made from the player's own games.
    /// </summary>
    public static IReadOnlyList<GameTree> BuildEndgame(GameTree game, IReadOnlyDictionary<GameNode, StudyVerdict> verdicts) =>
        Make(game, verdicts, endgame: true);

    private static IReadOnlyList<GameTree> Make(GameTree game, IReadOnlyDictionary<GameNode, StudyVerdict> verdicts, bool endgame)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(verdicts);
        var cursor = new GameCursor(game);
        int size = cursor.BoardSize;
        string players = $"{game.Info.BlackPlayer ?? Tr.T("Common.Black")}–{game.Info.WhitePlayer ?? Tr.T("Common.White")}";
        var problems = new List<(int Move, GameTree Tree)>();
        foreach ((GameNode node, StudyVerdict v) in verdicts)
        {
            if (v.Best is not { } best || node.Parent is not { } parent || node.GetMove(size) is not { Point: { } played } move || played == best)
            {
                continue;
            }

            int moveNumber = GameCursor.Path(node).Count(n => n.HasMove);
            bool inEndgame = GamePhases.Of(moveNumber, size) == GamePhase.Endgame;
            if (inEndgame != endgame
                || (endgame ? v.PointsLost < MinEndgamePointsLost : v.Quality is not (MoveQuality.Mistake or MoveQuality.Blunder) || v.PointsLost < MinPointsLost))
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
            problems.Add((number, TsumegoWriter.BestMove(before, move.Color, best, played, name, endgame ? "Tsumego.FindBiggest" : "Tsumego.FindBest", wrong)));
        }

        return [.. problems.OrderBy(p => p.Move).Select(p => p.Tree)];
    }
}
