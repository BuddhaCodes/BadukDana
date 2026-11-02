using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- "Explain this move" (KataGo) ----------

    /// <summary>The author of what KataGo writes into a study (notes, replies, its lines).</summary>
    public const string KataGoAuthor = "KataGo";

    /// <summary>KataGo is set up and allowed (never during your live OGS game), and the move on the board is a move.</summary>
    public bool CanExplain => ExplainAvailable && !IsBusy && _game.CurrentNode.GetMove(_game.Cursor.BoardSize) is not null;

    /// <summary>The button is shown: KataGo is set up and allowed here.</summary>
    public bool ExplainAvailable => _analysis is { IsEngineActive: true } && !IsLiveOnline;

    private void ExplainChanged()
    {
        OnPropertyChanged(nameof(ExplainAvailable));
        ExplainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// "Explain this move": KataGo reads the move on the board and writes what it found into the study — the points
    /// lost and the better move in a note (a reply when the move already has one) and, when there was something
    /// better, its line as "what if" stones the player can edit. Never during your live OGS game.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExplain))]
    private async Task Explain()
    {
        if (_analysis is not { IsEngineActive: true } analysis || IsLiveOnline)
        {
            _game.ShowStatus(Tr.T("Study.ExplainUnavailable"));
            return;
        }

        GameNode node = _game.CurrentNode;
        IsBusy = true;
        _game.ShowStatus(Tr.T("Study.Explaining"));
        MoveExplanation? explanation;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            explanation = await analysis.ExplainAsync(node, cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or EngineException)
        {
            explanation = null;
        }
        finally
        {
            IsBusy = false;
        }

        // The game may have changed meanwhile (another game opened, your OGS game started): write nothing then.
        if (explanation is null || IsLiveOnline || !ReferenceEquals(Root(node), _game.Tree.Root))
        {
            _game.ShowStatus(Tr.T("Study.ExplainUnavailable"));
            return;
        }

        // The line is drawn on the move itself: it stops before a point the move's own position already holds.
        BoardState after = _game.Cursor.GetBoard(node);
        explanation = explanation with { Line = [.. explanation.Line.TakeWhile(p => after.IsOnBoard(p) && after[p] == Stone.Empty)] };
        int height = _game.Board.Height;
        string text = ExplanationText(explanation, height);
        DateTimeOffset now = _time.GetUtcNow();
        StudyStore.Write(node, Explained(StudyStore.Read(node), explanation, text, now));
        _game.StudyEdited();
        Sync();
        IsActive = true;
        _game.ShowStatus(text);
    }

    /// <summary>"KataGo: mistake, −3.2 points. Better: D4 (its line is drawn)." / "KataGo: the best move."</summary>
    public static string ExplanationText(MoveExplanation explanation, int height)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        StudyVerdict v = explanation.Verdict;
        string quality = AnalysisViewModel.QualityName(v.Quality);
        if (v.Quality is MoveQuality.Best or MoveQuality.Excellent || v.Best is not { } best)
        {
            return Tr.F("Study.ExplainGood", quality);
        }

        string lost = v.PointsLost.ToString("0.0", CultureInfo.CurrentCulture);
        string winrate = (explanation.WinrateLost * 100).ToString("0", CultureInfo.CurrentCulture);
        return explanation.WinrateLost >= 0.01
            ? Tr.F("Study.ExplainLostWinrate", quality, lost, best.ToHuman(height), winrate)
            : Tr.F("Study.ExplainLost", quality, lost, best.ToHuman(height));
    }

    /// <summary>The study of the move with KataGo's note (or reply) and its line, replacing KataGo's earlier line.</summary>
    internal static NodeStudy Explained(NodeStudy study, MoveExplanation explanation, string text, DateTimeOffset now)
    {
        List<StudyPin> pins = [.. study.Pins];
        if (pins.Count > 0)
        {
            StudyPin first = pins[0];
            pins[0] = first with { Replies = [.. first.Replies.Where(r => r.Author != KataGoAuthor), new StudyReply(KataGoAuthor, now, text)] };
        }
        else
        {
            PinCategory category = explanation.Verdict.Quality switch
            {
                MoveQuality.Best or MoveQuality.Excellent => PinCategory.GoodMove,
                MoveQuality.Good => PinCategory.Idea,
                _ => PinCategory.Mistake,
            };
            pins.Add(new StudyPin(StudyStore.NewId(), category, KataGoAuthor, now, text, []));
        }

        List<StudyDrawing> drawings = [.. study.Drawings.Where(d => !(d.Kind == DrawingKind.Sequence && d.Author == KataGoAuthor))];
        if (explanation.Verdict.Quality is not (MoveQuality.Best or MoveQuality.Excellent) && explanation.Line.Count > 0
            && explanation.Line[0] != explanation.Played)
        {
            drawings.Add(new StudyDrawing(
                StudyStore.NewId(),
                DrawingKind.Sequence,
                KataGoAuthor,
                StudyColor.Green,
                explanation.Line,
                explanation.Mover == Stone.White ? "W" : "B"));
        }

        return study with { Pins = pins, Drawings = drawings };
    }

    private static GameNode Root(GameNode node)
    {
        GameNode at = node;
        while (at.Parent is { } parent)
        {
            at = parent;
        }

        return at;
    }
}
