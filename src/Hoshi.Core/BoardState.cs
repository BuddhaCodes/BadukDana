using System.Collections.Immutable;
using System.Text;

namespace Hoshi.Core;

/// <summary>
/// Immutable Go position: stones, prisoners, ko point, player to move and the position history needed for superko.
/// Every move returns a new instance; existing instances never change, so they can be shared freely between threads,
/// variations in a game tree and undo stacks.
/// </summary>
public sealed class BoardState
{
    public const int MinSize = 2;
    public const int MaxSize = 25;

    private readonly Stone[] _cells;
    private readonly ImmutableHashSet<ulong> _history;
    private readonly Stone _koForbiddenFor;

    private BoardState(
        int width,
        int height,
        RuleSet rules,
        Stone[] cells,
        ulong hash,
        Stone toMove,
        int blackCaptures,
        int whiteCaptures,
        Point? koPoint,
        Stone koForbiddenFor,
        ImmutableHashSet<ulong> history)
    {
        Width = width;
        Height = height;
        Rules = rules;
        _cells = cells;
        Hash = hash;
        ToMove = toMove;
        BlackCaptures = blackCaptures;
        WhiteCaptures = whiteCaptures;
        KoPoint = koPoint;
        _koForbiddenFor = koForbiddenFor;
        _history = history;
    }

    public int Width { get; }

    public int Height { get; }

    public RuleSet Rules { get; }

    /// <summary>Zobrist hash of the stones on the board (independent of captures, ko and player to move).</summary>
    public ulong Hash { get; }

    /// <summary>The player expected to move next. Moves of either colour are accepted (as SGF allows).</summary>
    public Stone ToMove { get; }

    /// <summary>Stones captured by Black (White's stones in Black's prisoner bowl).</summary>
    public int BlackCaptures { get; }

    /// <summary>Stones captured by White.</summary>
    public int WhiteCaptures { get; }

    /// <summary>Point that the player to move may not play because of the simple ko rule, if any.</summary>
    public Point? KoPoint { get; }

    public IEnumerable<Point> AllPoints
    {
        get
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    yield return new Point(x, y);
                }
            }
        }
    }

    public Stone this[Point point] => this[point.X, point.Y];

    public Stone this[int x, int y] =>
        IsOnBoard(x, y) ? _cells[(y * Width) + x] : throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is off the board.");

    public static BoardState Create(int size, RuleSet? rules = null) => Create(size, size, rules);

    public static BoardState Create(int width, int height, RuleSet? rules = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSize);

        rules ??= RuleSet.Japanese;
        var history = ImmutableHashSet.Create(HistoryKey(rules, 0UL, Stone.Black));
        return new BoardState(width, height, rules, new Stone[width * height], 0UL, Stone.Black, 0, 0, null, Stone.Empty, history);
    }

    public int CapturesBy(Stone color) => color switch
    {
        Stone.Black => BlackCaptures,
        Stone.White => WhiteCaptures,
        _ => 0,
    };

    public bool IsOnBoard(Point point) => IsOnBoard(point.X, point.Y);

    public bool IsOnBoard(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public IEnumerable<Point> Neighbors(Point point)
    {
        if (point.X > 0)
        {
            yield return point with { X = point.X - 1 };
        }

        if (point.X < Width - 1)
        {
            yield return point with { X = point.X + 1 };
        }

        if (point.Y > 0)
        {
            yield return point with { Y = point.Y - 1 };
        }

        if (point.Y < Height - 1)
        {
            yield return point with { Y = point.Y + 1 };
        }
    }

    /// <summary>All stones connected to the stone at <paramref name="point"/>; empty for an empty point.</summary>
    public IReadOnlyList<Point> GetGroup(Point point) =>
        this[point] == Stone.Empty ? [] : FindGroup(_cells, point);

    public IReadOnlyList<Point> GetLiberties(Point point) =>
        this[point] == Stone.Empty ? [] : Liberties(_cells, FindGroup(_cells, point));

    public int CountLiberties(Point point) => GetLiberties(point).Count;

    public bool IsLegal(Stone color, Point point) => TryPlay(color, point).IsLegal;

    /// <summary>Plays a move, returning the new position or the reason it is illegal.</summary>
    public MoveResult TryPlay(Stone color, Point point)
    {
        if (color == Stone.Empty)
        {
            throw new ArgumentException("A move needs a colour (Black or White).", nameof(color));
        }

        if (!IsOnBoard(point))
        {
            return MoveResult.Illegal(IllegalMoveReason.OutOfBounds);
        }

        if (this[point] != Stone.Empty)
        {
            return MoveResult.Illegal(IllegalMoveReason.Occupied);
        }

        if (KoPoint == point && color == _koForbiddenFor)
        {
            return MoveResult.Illegal(IllegalMoveReason.Ko);
        }

        Stone opponent = color.Opponent();
        var cells = (Stone[])_cells.Clone();
        cells[Index(point)] = color;
        ulong hash = Hash ^ Zobrist.Key(point.X, point.Y, color);

        var captured = new List<Point>();
        foreach (Point n in Neighbors(point))
        {
            if (cells[Index(n)] != opponent)
            {
                continue;
            }

            List<Point> group = FindGroup(cells, n);
            if (!HasLiberty(cells, group))
            {
                hash = Remove(cells, group, opponent, hash);
                captured.AddRange(group);
            }
        }

        List<Point> own = FindGroup(cells, point);
        int ownLiberties = Liberties(cells, own).Count;
        bool suicide = ownLiberties == 0;
        if (suicide)
        {
            if (!Rules.AllowSuicide)
            {
                return MoveResult.Illegal(IllegalMoveReason.Suicide);
            }

            hash = Remove(cells, own, color, hash);
        }

        Point? ko = !suicide && captured.Count == 1 && own.Count == 1 && ownLiberties == 1 ? captured[0] : null;

        int blackCaptures = BlackCaptures;
        int whiteCaptures = WhiteCaptures;
        Stone scorer = suicide ? opponent : color;
        int removed = suicide ? own.Count : captured.Count;
        if (scorer == Stone.Black)
        {
            blackCaptures += removed;
        }
        else
        {
            whiteCaptures += removed;
        }

        ulong key = HistoryKey(Rules, hash, opponent);
        if (Rules.Ko != KoRule.Simple && _history.Contains(key))
        {
            return MoveResult.Illegal(IllegalMoveReason.Superko);
        }

        var next = new BoardState(
            Width, Height, Rules, cells, hash, opponent, blackCaptures, whiteCaptures,
            ko, ko is null ? Stone.Empty : opponent, _history.Add(key));

        return MoveResult.Legal(next, suicide ? own : captured);
    }

    /// <summary>Plays a move and throws <see cref="IllegalMoveException"/> if it is illegal.</summary>
    public BoardState Play(Stone color, Point point)
    {
        MoveResult result = TryPlay(color, point);
        return result.State ?? throw new IllegalMoveException(color, point, result.Reason!.Value);
    }

    public BoardState Pass(Stone color)
    {
        if (color == Stone.Empty)
        {
            throw new ArgumentException("A pass needs a colour (Black or White).", nameof(color));
        }

        return new BoardState(
            Width, Height, Rules, _cells, Hash, color.Opponent(), BlackCaptures, WhiteCaptures,
            null, Stone.Empty, _history);
    }

    /// <summary>
    /// Places or removes stones without applying capture rules (SGF <c>AB</c>/<c>AW</c>/<c>AE</c>, editors, handicap).
    /// Clears any ko and records the resulting position in the superko history.
    /// </summary>
    public BoardState Setup(IEnumerable<(Point Point, Stone Stone)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var cells = (Stone[])_cells.Clone();
        ulong hash = Hash;
        foreach ((Point p, Stone s) in changes)
        {
            if (!IsOnBoard(p))
            {
                throw new ArgumentOutOfRangeException(nameof(changes), $"{p} is off the board.");
            }

            int i = Index(p);
            hash ^= Zobrist.Key(p.X, p.Y, cells[i]) ^ Zobrist.Key(p.X, p.Y, s);
            cells[i] = s;
        }

        return new BoardState(
            Width, Height, Rules, cells, hash, ToMove, BlackCaptures, WhiteCaptures,
            null, Stone.Empty, _history.Add(HistoryKey(Rules, hash, ToMove)));
    }

    public BoardState WithToMove(Stone color)
    {
        if (color == Stone.Empty)
        {
            throw new ArgumentException("The player to move must be Black or White.", nameof(color));
        }

        return color == ToMove
            ? this
            : new BoardState(
                Width, Height, Rules, _cells, Hash, color, BlackCaptures, WhiteCaptures,
                KoPoint, _koForbiddenFor, _history.Add(HistoryKey(Rules, Hash, color)));
    }

    /// <summary>Free handicap placement: black stones on the given points, White to move.</summary>
    public BoardState WithHandicap(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Point[] list = points.ToArray();

        if (list.Length < 2)
        {
            throw new ArgumentException("A handicap needs at least two stones.", nameof(points));
        }

        if (list.Distinct().Count() != list.Length)
        {
            throw new ArgumentException("Handicap points must be distinct.", nameof(points));
        }

        foreach (Point p in list)
        {
            if (!IsOnBoard(p))
            {
                throw new ArgumentOutOfRangeException(nameof(points), $"{p} is off the board.");
            }

            if (this[p] != Stone.Empty)
            {
                throw new ArgumentException($"{p} is already occupied.", nameof(points));
            }
        }

        return Setup(list.Select(p => (p, Stone.Black))).WithToMove(Stone.White);
    }

    /// <summary>Fixed handicap on the traditional star points (square boards only).</summary>
    public BoardState WithFixedHandicap(int stones)
    {
        if (Width != Height)
        {
            throw new InvalidOperationException("Fixed handicap is only defined for square boards.");
        }

        return WithHandicap(Handicap.FixedPoints(Width, stones));
    }

    public override string ToString()
    {
        var sb = new StringBuilder((Width + 1) * Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                sb.Append(_cells[(y * Width) + x] switch
                {
                    Stone.Black => 'X',
                    Stone.White => 'O',
                    _ => '.',
                });
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    internal Stone[] CopyCells() => (Stone[])_cells.Clone();

    private static ulong HistoryKey(RuleSet rules, ulong hash, Stone toMove) =>
        rules.Ko == KoRule.SituationalSuperko && toMove == Stone.White ? hash ^ Zobrist.WhiteToMove : hash;

    private int Index(Point p) => (p.Y * Width) + p.X;

    private ulong Remove(Stone[] cells, List<Point> group, Stone color, ulong hash)
    {
        foreach (Point p in group)
        {
            cells[Index(p)] = Stone.Empty;
            hash ^= Zobrist.Key(p.X, p.Y, color);
        }

        return hash;
    }

    internal List<Point> FindGroup(Stone[] cells, Point start)
    {
        Stone color = cells[Index(start)];
        var group = new List<Point>();
        var visited = new bool[cells.Length];
        var stack = new Stack<Point>();
        stack.Push(start);
        visited[Index(start)] = true;

        while (stack.Count > 0)
        {
            Point p = stack.Pop();
            group.Add(p);
            foreach (Point n in Neighbors(p))
            {
                int i = Index(n);
                if (!visited[i] && cells[i] == color)
                {
                    visited[i] = true;
                    stack.Push(n);
                }
            }
        }

        return group;
    }

    private bool HasLiberty(Stone[] cells, List<Point> group)
    {
        foreach (Point p in group)
        {
            foreach (Point n in Neighbors(p))
            {
                if (cells[Index(n)] == Stone.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<Point> Liberties(Stone[] cells, List<Point> group)
    {
        var seen = new HashSet<Point>();
        foreach (Point p in group)
        {
            foreach (Point n in Neighbors(p))
            {
                if (cells[Index(n)] == Stone.Empty)
                {
                    seen.Add(n);
                }
            }
        }

        return [.. seen];
    }
}
