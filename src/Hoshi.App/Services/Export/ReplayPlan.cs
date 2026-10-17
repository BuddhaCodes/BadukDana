using System.Globalization;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Services.Export;

/// <summary>The shape of a replay video: 16:9 (YouTube, X, Discord), square (Instagram) or vertical (stories, shorts).</summary>
public enum ReplayFormat
{
    Wide,
    Square,
    Vertical,
}

/// <summary>What kind of picture a step of the replay is.</summary>
public enum ReplayStepKind
{
    /// <summary>The title card: players, date, the empty (or starting) board.</summary>
    Intro,

    Move,

    /// <summary>The final position with the result.</summary>
    Outro,
}

/// <summary>The sound under a step (stone clicks and captures, from Hoshi's own sounds).</summary>
public enum ReplaySound
{
    None,
    Stone,
    Capture,
}

/// <summary>How the replay is made. Move numbers count moves on the line (1 = the first move).</summary>
public sealed record ReplayOptions
{
    public int FromMove { get; init; } = 1;

    public int ToMove { get; init; } = int.MaxValue;

    /// <summary>How long an ordinary move stays (seconds).</summary>
    public double SecondsPerMove { get; init; } = 0.8;

    public ReplayFormat Format { get; init; } = ReplayFormat.Wide;

    public bool Intro { get; init; } = true;

    /// <summary>Study notes and comments under the moves that have them (they stay longer).</summary>
    public bool Notes { get; init; } = true;

    /// <summary>KataGo's big mistakes and captures stay longer, with a label and a ring.</summary>
    public bool Highlights { get; init; } = true;

    public bool Sounds { get; init; } = true;

    public bool Coordinates { get; init; } = true;

    public ShareOptions Share { get; init; } = new();
}

/// <summary>One picture of the replay and how long it stays.</summary>
public sealed record ReplayStep(
    ReplayStepKind Kind,
    BoardState Board,
    Point? LastMove,
    int MoveNumber,
    string Title,
    string Caption,
    string? Highlight,
    TimeSpan Duration,
    ReplaySound Sound,
    IReadOnlyList<Markup>? Marks = null,
    GameNode? Node = null);

/// <summary>
/// The replay of a line of the game (the line through the shown move, to its end), cut to a range of moves: a title
/// card, one step per move (longer on captures, KataGo's mistakes and moves with notes) and the result.
/// </summary>
public static class ReplayPlan
{
    public static readonly TimeSpan IntroLength = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan OutroLength = TimeSpan.FromSeconds(3.5);

    /// <summary>Captures stay this many times longer.</summary>
    public const double CaptureFactor = 1.6;

    /// <summary>Mistakes and blunders stay this many times longer.</summary>
    public const double MistakeFactor = 3;

    public static IReadOnlyList<ReplayStep> Build(GameTree tree, GameNode through, ReplayOptions options, IReadOnlyDictionary<GameNode, StudyVerdict>? verdicts = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(through);
        ArgumentNullException.ThrowIfNull(options);
        var cursor = new GameCursor(tree);
        int size = cursor.BoardSize;
        GameNode end = through;
        while (end.Children.Count > 0)
        {
            end = end.Children[0];
        }

        IReadOnlyList<GameNode> path = GameCursor.Path(end);
        string black = options.Share.Black(tree), white = options.Share.White(tree);
        string title = $"{black} — {white}";
        double perMove = Math.Clamp(options.SecondsPerMove, 0.1, 10);
        int from = Math.Max(1, options.FromMove);
        var steps = new List<ReplayStep>();

        int number = 0;
        GameNode? beforeFirst = tree.Root;
        foreach (GameNode node in path)
        {
            if (node.HasMove && ++number == from)
            {
                break;
            }

            beforeFirst = node;
        }

        if (options.Intro)
        {
            var details = new List<string>();
            if (!options.Share.HideNames && tree.Info.Date is { Length: > 0 } date)
            {
                details.Add(date);
            }

            details.Add(string.Create(CultureInfo.InvariantCulture, $"{tree.Info.Width}×{tree.Info.Height}"));
            if (tree.Info.Komi is { } komi)
            {
                details.Add(string.Create(CultureInfo.InvariantCulture, $"komi {komi:0.#}"));
            }

            if (from > 1)
            {
                details.Add(Tr.F("Replay.FromMove", from));
            }

            steps.Add(new ReplayStep(ReplayStepKind.Intro, cursor.GetBoard(beforeFirst), null, from - 1, title, string.Join(" · ", details), null, IntroLength, ReplaySound.None, Node: beforeFirst));
        }

        number = 0;
        BoardState? previous = null;
        foreach (GameNode node in path)
        {
            BoardState board = cursor.GetBoard(node);
            if (!node.HasMove)
            {
                previous = board;
                continue;
            }

            number++;
            if (number < from || number > options.ToMove)
            {
                previous = board;
                continue;
            }

            SgfMove move = node.GetMove(size)!.Value;
            string who = Tr.T(move.Color == Stone.Black ? "Common.Black" : "Common.White");
            string where = move.Point is { } p ? p.ToHuman(board.Height) : Tr.T("Viewer.Pass");
            bool captured = previous is not null && board.BlackCaptures + board.WhiteCaptures > previous.BlackCaptures + previous.WhiteCaptures;
            double seconds = perMove;
            string? highlight = null;
            IReadOnlyList<Markup>? marks = null;

            if (options.Highlights && verdicts is not null && verdicts.TryGetValue(node, out StudyVerdict? v) && v.Quality is MoveQuality.Mistake or MoveQuality.Blunder)
            {
                highlight = Tr.T(v.Quality == MoveQuality.Blunder ? "Replay.Blunder" : "Replay.Mistake")
                    + (double.IsFinite(v.PointsLost) && v.PointsLost > 0 ? string.Create(CultureInfo.CurrentCulture, $" · −{v.PointsLost:0.0}") : string.Empty)
                    + (v.Best is { } best ? " · " + Tr.F("Analysis.Better", best.ToHuman(board.Height)) : string.Empty);
                seconds = perMove * MistakeFactor;
                var ring = new List<Markup>();
                if (move.Point is { } played)
                {
                    ring.Add(new Markup(played, MarkupKind.Cross));
                }

                if (v.Best is { } better)
                {
                    ring.Add(new Markup(better, MarkupKind.Circle));
                }

                marks = ring;
            }
            else if (options.Highlights && captured)
            {
                int stones = board.BlackCaptures + board.WhiteCaptures - (previous!.BlackCaptures + previous.WhiteCaptures);
                highlight = Tr.F("Replay.Captures", stones);
                seconds = perMove * CaptureFactor;
            }

            string caption = string.Empty;
            if (options.Notes && Note(node) is { Length: > 0 } note)
            {
                caption = note;
                seconds = Math.Max(seconds, Math.Min(7, 2.5 + (note.Length / 40.0)));
            }

            steps.Add(new ReplayStep(
                ReplayStepKind.Move,
                board,
                move.Point,
                number,
                Tr.F("Replay.MoveLine", number, who, where),
                caption,
                highlight,
                TimeSpan.FromSeconds(seconds),
                !options.Sounds ? ReplaySound.None : captured ? ReplaySound.Capture : move.Point is null ? ReplaySound.None : ReplaySound.Stone,
                marks,
                node));
            previous = board;
        }

        ReplayStep? last = steps.LastOrDefault(s => s.Kind == ReplayStepKind.Move);
        if (last is not null)
        {
            bool atEnd = last.MoveNumber == number;
            string result = atEnd && tree.Info.Result is { Length: > 0 } r ? Tr.F("Replay.Result", r) : Tr.F("Replay.MovesShown", steps.Count(s => s.Kind == ReplayStepKind.Move));
            steps.Add(last with
            {
                Kind = ReplayStepKind.Outro,
                Title = title,
                Caption = result,
                Highlight = null,
                Marks = null,
                Duration = OutroLength,
                Sound = ReplaySound.None,
            });
        }

        return steps;
    }

    /// <summary>The whole length of a replay.</summary>
    public static TimeSpan Length(IEnumerable<ReplayStep> steps) => TimeSpan.FromTicks(steps.Sum(s => s.Duration.Ticks));

    // The first study note, else the comment without Hoshi's KataGo lines.
    private static string Note(GameNode node)
    {
        if (StudyStore.Read(node).Pins.FirstOrDefault(p => p.Text.Length > 0) is { } pin)
        {
            return pin.Text.Trim();
        }

        return string.Join("\n", (node.Comment ?? string.Empty).Split('\n').Where(l => !l.StartsWith(ViewModels.AnalysisViewModel.ReviewPrefix, StringComparison.Ordinal))).Trim();
    }
}
