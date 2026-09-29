using Hoshi.Core;

namespace Hoshi.Sgf;

/// <summary>
/// A position within a <see cref="GameTree"/>: navigation, lazily computed (and cached) board states, and the
/// edit operations used by the UI. All edits go through the cursor so it can invalidate cached boards.
/// </summary>
public sealed class GameCursor
{
    private readonly Dictionary<GameNode, BoardState> _boards = [];
    private readonly Dictionary<GameNode, GameNode> _lastVisitedChild = [];
    private readonly List<string> _warnings = [];
    private readonly RuleSet _rules;
    private GameNode _current;

    public GameCursor(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Tree = tree;
        _current = tree.Root;
        _rules = tree.Info.Rules ?? RuleSet.Japanese;
        if (tree.Info.Width > BoardState.MaxSize || tree.Info.Height > BoardState.MaxSize)
        {
            throw new NotSupportedException(
                $"Boards larger than {BoardState.MaxSize}×{BoardState.MaxSize} are not supported ({tree.Info.Width}×{tree.Info.Height}).");
        }
    }

    /// <summary>Raised after the current node changes or the tree is edited.</summary>
    public event EventHandler? Changed;

    public GameTree Tree { get; }

    public GameNode Current => _current;

    public BoardState Board => GetBoard(_current);

    /// <summary>Problems found while replaying the file, such as illegal moves that had to be forced.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public int BoardSize => Math.Max(Tree.Info.Width, Tree.Info.Height);

    /// <summary>The move played in the current node, or null for setup nodes and passes.</summary>
    public Point? LastMove => _current.GetMove(BoardSize)?.Point;

    /// <summary>Number of move nodes (including passes) from the root to the current node.</summary>
    public int MoveNumber => Path(_current).Count(n => n.HasMove);

    public bool CanGoBack => _current.Parent is not null;

    public bool CanGoForward => _current.Children.Count > 0;

    public bool IsOnMainLine => Path(_current).All(n => n.Parent is null || n.Parent.Children[0] == n);

    public string? Comment
    {
        get => _current.Comment;
        set
        {
            if (_current.Comment == (string.IsNullOrWhiteSpace(value) ? null : value))
            {
                return;
            }

            _current.Comment = value;
            OnChanged();
        }
    }

    /// <summary>Nodes from the root to <paramref name="node"/> inclusive.</summary>
    public static IReadOnlyList<GameNode> Path(GameNode node)
    {
        var path = new List<GameNode>();
        for (GameNode? n = node; n is not null; n = n.Parent)
        {
            path.Add(n);
        }

        path.Reverse();
        return path;
    }

    public BoardState GetBoard(GameNode node)
    {
        if (_boards.TryGetValue(node, out BoardState? cached))
        {
            return cached;
        }

        // Walk up to the nearest cached ancestor, then replay downwards (iteratively, so long games are fine).
        var pending = new Stack<GameNode>();
        GameNode? n = node;
        BoardState? state = null;
        while (n is not null && !_boards.TryGetValue(n, out state))
        {
            pending.Push(n);
            n = n.Parent;
        }

        while (pending.Count > 0)
        {
            GameNode next = pending.Pop();
            state = Apply(next, state);
            _boards[next] = state;
        }

        return state!;
    }

    public bool GoTo(GameNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node == _current)
        {
            return false;
        }

        if (Path(node)[0] != Tree.Root)
        {
            throw new ArgumentException("The node does not belong to this game.", nameof(node));
        }

        foreach (GameNode step in Path(node).Skip(1))
        {
            _lastVisitedChild[step.Parent!] = step;
        }

        _current = node;
        OnChanged();
        return true;
    }

    public bool Next()
    {
        if (_current.Children.Count == 0)
        {
            return false;
        }

        GameNode child = _lastVisitedChild.TryGetValue(_current, out GameNode? remembered) && remembered.Parent == _current
            ? remembered
            : _current.Children[0];
        return GoTo(child);
    }

    public bool Previous() => _current.Parent is { } parent && GoTo(parent);

    public bool First() => GoTo(Tree.Root);

    /// <summary>Follows the current line (remembered variations, else the main line) to its end.</summary>
    public bool Last()
    {
        GameNode node = _current;
        while (node.Children.Count > 0)
        {
            node = _lastVisitedChild.TryGetValue(node, out GameNode? remembered) && remembered.Parent == node
                ? remembered
                : node.Children[0];
        }

        return GoTo(node);
    }

    public bool NextVariation() => SwitchSibling(+1);

    public bool PreviousVariation() => SwitchSibling(-1);

    /// <summary>
    /// Plays a move (or a pass when <paramref name="point"/> is null) for the player to move. An existing child with the
    /// same move is reused; otherwise a new variation is added after the existing ones.
    /// </summary>
    public MoveResult Play(Point? point)
    {
        BoardState board = Board;
        Stone color = board.ToMove;
        MoveResult result;
        if (point is { } p)
        {
            result = board.TryPlay(color, p);
            if (!result.IsLegal)
            {
                return result;
            }
        }
        else
        {
            result = MoveResult.ForPass(board.Pass(color));
        }

        string id = color == Stone.Black ? "B" : "W";
        string value = point?.ToSgf() ?? string.Empty;
        GameNode? existing = _current.Children.FirstOrDefault(c =>
            c.GetMove(BoardSize) is { } m && m.Color == color && m.Point == point);

        if (existing is null)
        {
            existing = _current.AddChild();
            existing.SetValue(id, value);
        }

        GoTo(existing);
        return result;
    }

    /// <summary>
    /// Adds or removes a setup stone (AB/AW/AE). On a node that already has a move, a new child node is created first.
    /// </summary>
    public void ToggleSetupStone(Point point, Stone color)
    {
        if (color == Stone.Empty)
        {
            throw new ArgumentException("Use Black or White.", nameof(color));
        }

        if (_current.HasMove)
        {
            GameNode child = _current.AddChild();
            _lastVisitedChild[_current] = child;
            _current = child;
        }

        GameNode node = _current;
        BoardState before = Board;
        string sgf = point.ToSgf();
        bool removing = before[point] == color;

        foreach (string id in new[] { "AB", "AW", "AE" })
        {
            RemovePoint(node, id, point);
        }

        BoardState parentBoard = node.Parent is { } parent ? GetBoard(parent) : EmptyBoard();
        if (removing)
        {
            if (parentBoard[point] != Stone.Empty)
            {
                node.AddValue("AE", sgf);
            }
        }
        else if (parentBoard[point] != color)
        {
            node.AddValue(color == Stone.Black ? "AB" : "AW", sgf);
        }

        Invalidate(node);
        OnChanged();
    }

    /// <summary>Toggles a markup shape on the current node; a different shape on the same point is replaced.</summary>
    public void ToggleMarkup(Point point, MarkupKind kind, string? label = null)
    {
        GameNode node = _current;
        Markup? existing = node.GetMarkup().FirstOrDefault(m => m.Point == point);

        foreach ((string id, _) in GameNode.MarkupProperties)
        {
            RemovePoint(node, id, point);
        }

        foreach (string v in node.GetValues("LB").Where(v => v.StartsWith(point.ToSgf() + ":", StringComparison.Ordinal)).ToList())
        {
            node.RemoveValue("LB", v);
        }

        if (existing is null || existing.Kind != kind)
        {
            if (kind == MarkupKind.Label)
            {
                node.AddValue("LB", $"{point.ToSgf()}:{label ?? NextFreeLabel()}");
            }
            else
            {
                node.AddValue(GameNode.MarkupProperties.First(m => m.Kind == kind).Id, point.ToSgf());
            }
        }

        OnChanged();
    }

    /// <summary>The first capital letter not yet used as a label on the current node.</summary>
    public string NextFreeLabel()
    {
        var used = _current.GetMarkup().Where(m => m.Kind == MarkupKind.Label).Select(m => m.Text).ToHashSet();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!used.Contains(c.ToString()))
            {
                return c.ToString();
            }
        }

        for (int i = 1; ; i++)
        {
            string s = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(s))
            {
                return s;
            }
        }
    }

    /// <summary>Removes the current node and its subtree, moving to the parent. The root cannot be deleted.</summary>
    public bool DeleteCurrent()
    {
        if (_current.Parent is not { } parent)
        {
            return false;
        }

        GameNode removed = _current;
        Invalidate(removed);
        removed.Detach();
        _lastVisitedChild.Remove(parent);
        _current = parent;
        OnChanged();
        return true;
    }

    /// <summary>Reorders ancestors so the current line becomes the main line (first child at every fork).</summary>
    public void PromoteToMainLine()
    {
        foreach (GameNode node in Path(_current).Skip(1))
        {
            node.Parent!.MoveChild(node, 0);
        }

        OnChanged();
    }

    /// <summary>Call after editing properties of a node directly, so cached boards of it and its descendants are rebuilt.</summary>
    public void Invalidate(GameNode node)
    {
        foreach (GameNode n in node.Descendants())
        {
            _boards.Remove(n);
        }
    }

    /// <summary>Raises <see cref="Changed"/> after an external edit (e.g. the game-info dialog).</summary>
    public void NotifyEdited() => OnChanged();

    private BoardState EmptyBoard() => BoardState.Create(Tree.Info.Width, Tree.Info.Height, _rules);

    private BoardState Apply(GameNode node, BoardState? parentState)
    {
        BoardState state = parentState ?? EmptyBoard();

        var setup = new List<(Point, Stone)>();
        setup.AddRange(node.GetPoints("AE").Select(p => (p, Stone.Empty)));
        setup.AddRange(node.GetPoints("AB").Select(p => (p, Stone.Black)));
        setup.AddRange(node.GetPoints("AW").Select(p => (p, Stone.White)));
        setup.RemoveAll(s => !state.IsOnBoard(s.Item1));
        if (setup.Count > 0)
        {
            state = state.Setup(setup);
        }

        if (node.IsRoot && Tree.Info.Handicap >= 2 && node.HasProperty("AB") && !node.HasProperty("PL"))
        {
            state = state.WithToMove(Stone.White);
        }

        if (node.GetMove(BoardSize) is { } move)
        {
            if (move.Point is not { } p)
            {
                state = state.Pass(move.Color);
            }
            else if (!state.IsOnBoard(p))
            {
                _warnings.Add($"Move {p.ToSgf()} is off the board; ignored.");
            }
            else
            {
                MoveResult r = state.TryPlay(move.Color, p);
                if (r.State is { } next)
                {
                    state = next;
                }
                else
                {
                    _warnings.Add($"Illegal move {move.Color} {p.ToSgf()} ({r.Reason}); placed without rules.");
                    state = state.Setup([(p, move.Color)]).WithToMove(move.Color.Opponent());
                }
            }
        }

        if (node.GetValue("PL") is { } pl)
        {
            state = state.WithToMove(pl.StartsWith('W') || pl.StartsWith('w') ? Stone.White : Stone.Black);
        }

        return state;
    }

    private bool SwitchSibling(int delta)
    {
        if (_current.Parent is not { } parent)
        {
            return false;
        }

        int index = parent.Children.ToList().IndexOf(_current) + delta;
        return index >= 0 && index < parent.Children.Count && GoTo(parent.Children[index]);
    }

    private static void RemovePoint(GameNode node, string id, Point point)
    {
        IReadOnlyList<Point> points = node.GetPoints(id);
        if (!points.Contains(point))
        {
            return;
        }

        // Rewrite the list without compression so a single point can be removed from a rectangle.
        node.SetValues(id, points.Where(p => p != point).Select(p => p.ToSgf()));
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
