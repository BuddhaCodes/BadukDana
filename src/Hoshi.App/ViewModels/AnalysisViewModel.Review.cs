using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

public sealed partial class AnalysisViewModel
{
    /// <summary>
    /// Writes KataGo's review into the game: <c>BM</c> on mistakes and blunders, <c>DO</c> on inaccuracies, and a
    /// line in the comment ("[KataGo] Mistake (−3.2) · better Q16"). Running it again replaces the old lines.
    /// Returns how many moves were judged; 0 when the engine is not available.
    /// </summary>
    public async Task<int> AnnotateMainLineAsync(CancellationToken cancellationToken)
    {
        var nodes = new List<GameNode>();
        for (GameNode n = _game.Tree.Root; n.Children.Count > 0;)
        {
            n = n.Children[0];
            if (n.GetMove(_game.Cursor.BoardSize) is { IsPass: false })
            {
                nodes.Add(n);
            }
        }

        IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict> verdicts = await VerdictsAsync(nodes, cancellationToken);
        LastReview = verdicts;
        foreach ((GameNode node, Services.Study.StudyVerdict v) in verdicts)
        {
            node.RemoveProperty("BM");
            node.RemoveProperty("DO");
            if (v.Quality is MoveQuality.Mistake or MoveQuality.Blunder)
            {
                node.SetValue("BM", v.Quality == MoveQuality.Blunder ? "2" : "1");
            }
            else if (v.Quality == MoveQuality.Inaccuracy)
            {
                node.SetValue("DO", string.Empty);
            }

            string line = ReviewPrefix + QualityName(v.Quality)
                + (v.PointsLost >= 0.05 && v.Quality != MoveQuality.Best ? string.Create(CultureInfo.InvariantCulture, $" (−{v.PointsLost:0.0})") : string.Empty)
                + (v.Best is { } best && v.Quality is not (MoveQuality.Best or MoveQuality.Excellent) ? " · " + Tr.F("Analysis.Better", best.ToHuman(_game.Board.Height)) : string.Empty);
            string kept = string.Join("\n", (node.Comment ?? string.Empty).Split('\n').Where(l => !l.StartsWith(ReviewPrefix, StringComparison.Ordinal))).Trim();
            node.Comment = kept.Length == 0 ? line : kept + "\n" + line;
        }

        if (verdicts.Count > 0)
        {
            _game.ReviewAnnotated();
        }

        return verdicts.Count;
    }

    /// <summary>KataGo's full verdicts (points lost, criticality) on the moves of the shown line it has read.</summary>
    public IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict> LineVerdicts { get; private set; } = new Dictionary<GameNode, Services.Study.StudyVerdict>();

    /// <summary>KataGo's verdicts of the last review written into a game (for the summaries).</summary>
    public IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict> LastReview { get; private set; } = new Dictionary<GameNode, Services.Study.StudyVerdict>();

    /// <summary>The start of the comment lines Hoshi writes (and replaces) with KataGo's review.</summary>
    public const string ReviewPrefix = "[KataGo] ";

    private void SetMistakes(IReadOnlyList<(int Number, GameNode Node, MoveAssessment Judged)> mistakes)
    {
        int height = _game.Board.Height;
        KeyMistake[] key = [.. mistakes
            .OrderByDescending(m => m.Judged.PointsLost)
            .Take(3)
            .OrderBy(m => m.Number)
            .Select(m => new KeyMistake(
                m.Node,
                m.Number,
                m.Judged.Played.Color == Stone.Black,
                m.Judged.Played.Point is { } p ? p.ToHuman(height) : Tr.T("Game.PassNoun"),
                m.Judged.Best.Point is { } b ? Tr.F("Analysis.Better", b.ToHuman(height)) : string.Empty,
                string.Create(CultureInfo.InvariantCulture, $"−{m.Judged.PointsLost:0.0}"),
                m.Judged.Quality == MoveQuality.Blunder))];
        int[] marks = [.. mistakes.Select(m => m.Number)];
        _mistakeNodes = [.. mistakes.Select(m => (m.Number, m.Node))];
        if (!marks.SequenceEqual(MistakeMarks))
        {
            MistakeMarks = marks;
        }

        if (key.Length != KeyMistakes.Count || key.Where((k, i) => k != KeyMistakes[i]).Any())
        {
            KeyMistakes = key;
        }
        else
        {
            NextMistakeCommand.NotifyCanExecuteChanged();
        }
    }
}
