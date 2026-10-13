using Hoshi.Core;

namespace Hoshi.Sgf.Problems;

/// <summary>What a move in a problem did.</summary>
public enum TsumegoOutcome
{
    /// <summary>Not a legal move: nothing happened.</summary>
    Illegal,

    /// <summary>A right move; the opponent answered and the problem goes on.</summary>
    Continue,

    /// <summary>The problem is solved.</summary>
    Solved,

    /// <summary>A wrong move (off the solution tree, or a branch marked wrong).</summary>
    Wrong,
}

/// <summary>
/// A problem (tsumego) read from an SGF game: the setup stones are the position, <c>PL</c> (or the first move)
/// says who plays, and the variations are the answers. A line is right when its last node's comment contains
/// "RIGHT" or "CORRECT" (the usual convention of problem collections) and wrong when a comment contains "WRONG".
/// </summary>
public sealed class TsumegoProblem
{
    private TsumegoProblem(GameTree tree, BoardState start, Stone toPlay)
    {
        Tree = tree;
        Start = start;
        ToPlay = toPlay;
    }

    public GameTree Tree { get; }

    public BoardState Start { get; }

    public Stone ToPlay { get; }

    public string Name => Tree.Info.GameName ?? string.Empty;

    /// <summary>The root comment: what the problem asks ("Black to live", "Capture"…).</summary>
    public string? Instructions => Tree.Root.Comment;

    /// <summary>A stable id (name and position), for progress kept between sessions.</summary>
    public string Id
    {
        get
        {
            // Name, size and setup stones: the same in every session and on every device.
            IEnumerable<string> Points(string id) => Tree.Root.GetValues(id).Order(StringComparer.Ordinal);
            return $"{Name}|{Tree.Info.Width}x{Tree.Info.Height}|{ToPlay}|B:{string.Join(",", Points("AB"))}|W:{string.Join(",", Points("AW"))}";
        }
    }

    public static TsumegoProblem FromTree(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var cursor = new GameCursor(tree);
        BoardState board = cursor.GetBoard(tree.Root);
        Stone toPlay = tree.Root.GetValue("PL") switch
        {
            "W" or "w" => Stone.White,
            "B" or "b" => Stone.Black,
            _ => tree.Root.Children.FirstOrDefault()?.GetMove(cursor.BoardSize)?.Color ?? Stone.Black,
        };
        return new TsumegoProblem(tree, board.WithToMove(toPlay), toPlay);
    }

    internal static bool IsRight(GameNode node) =>
        node.Comment is { } c && (c.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || c.Contains("CORRECT", StringComparison.OrdinalIgnoreCase))
        && !c.Contains("INCORRECT", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWrong(GameNode node) =>
        node.Comment is { } c && (c.Contains("WRONG", StringComparison.OrdinalIgnoreCase) || c.Contains("INCORRECT", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether some line from <paramref name="node"/> ends right.</summary>
    internal static bool LeadsRight(GameNode node) =>
        !IsWrong(node) && (node.Children.Count == 0 ? IsRight(node) : node.Children.Any(LeadsRight));
}

/// <summary>One try at a problem: the player's moves, the opponent's answers from the tree, the outcome.</summary>
public sealed class TsumegoAttempt
{
    private readonly int _size;
    private GameNode? _node;

    public TsumegoAttempt(TsumegoProblem problem)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        _node = problem.Tree.Root;
        _size = Math.Max(problem.Tree.Info.Width, problem.Tree.Info.Height);
        Board = problem.Start;
    }

    public TsumegoProblem Problem { get; }

    public BoardState Board { get; private set; }

    public Point? LastMove { get; private set; }

    public TsumegoOutcome? Result { get; private set; }

    public bool IsFinished => Result is TsumegoOutcome.Solved or TsumegoOutcome.Wrong;

    /// <summary>The comment of the last node reached (explanations of right and wrong lines).</summary>
    public string? Comment { get; private set; }

    /// <summary>The next move of a right line from here (the answer), or null when there is none.</summary>
    public Point? Hint => IsFinished || _node is null
        ? null
        : _node.Children.FirstOrDefault(c => c.GetMove(_size)?.Color == Problem.ToPlay && TsumegoProblem.LeadsRight(c))?.GetMove(_size)?.Point;

    public TsumegoOutcome Play(Point point)
    {
        if (IsFinished)
        {
            return Result!.Value;
        }

        MoveResult played = Board.TryPlay(Problem.ToPlay, point);
        if (!played.IsLegal)
        {
            return TsumegoOutcome.Illegal;
        }

        Board = played.State!;
        LastMove = point;
        GameNode? next = _node?.Children.FirstOrDefault(c => c.GetMove(_size) is { } m && m.Color == Problem.ToPlay && m.Point == point);
        if (next is null || TsumegoProblem.IsWrong(next) || !TsumegoProblem.LeadsRight(next))
        {
            Comment = next?.Comment;
            // Show the refutation when the collection has one.
            if (next?.Children.FirstOrDefault()?.GetMove(_size) is { Point: { } reply } refutation)
            {
                Answer(refutation.Color, reply);
                Comment = next.Children[0].Comment ?? Comment;
            }

            _node = null;
            return Finish(TsumegoOutcome.Wrong);
        }

        _node = next;
        Comment = next.Comment;
        if (next.Children.Count == 0)
        {
            return Finish(TsumegoOutcome.Solved);
        }

        // The opponent answers along a line that still ends right (the first one in the file).
        GameNode answer = next.Children.FirstOrDefault(TsumegoProblem.LeadsRight) ?? next.Children[0];
        if (answer.GetMove(_size) is { Point: { } p } m)
        {
            Answer(m.Color, p);
        }

        _node = answer;
        Comment = answer.Comment ?? Comment;
        return answer.Children.Count == 0
            ? Finish(TsumegoProblem.IsRight(answer) ? TsumegoOutcome.Solved : TsumegoOutcome.Wrong)
            : TsumegoOutcome.Continue;
    }

    private void Answer(Stone color, Point point)
    {
        MoveResult r = Board.TryPlay(color, point);
        if (r.IsLegal)
        {
            Board = r.State!;
            LastMove = point;
        }
    }

    private TsumegoOutcome Finish(TsumegoOutcome outcome)
    {
        Result = outcome;
        return outcome;
    }
}
