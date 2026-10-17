namespace Hoshi.Core;

/// <summary>
/// A local position to look for in games (like Kombilo): every point of a rectangle of the board, stones and empty
/// points alike. It matches in any of the board's symmetries and with the colours swapped. A pattern touching an
/// edge stays on that edge (a corner pattern is found in the four corners); one away from the edges is found anywhere.
/// </summary>
public sealed class BoardPattern
{
    private readonly Stone[] _cells;
    private readonly bool _left;
    private readonly bool _top;
    private readonly bool _right;
    private readonly bool _bottom;

    private BoardPattern(int boardWidth, int boardHeight, int x, int y, int width, int height, Stone[] cells)
    {
        BoardWidth = boardWidth;
        BoardHeight = boardHeight;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        _cells = cells;
        _left = x == 0;
        _top = y == 0;
        _right = x + width == boardWidth;
        _bottom = y + height == boardHeight;
        Stones = cells.Count(c => c != Stone.Empty);
    }

    public int BoardWidth { get; }

    public int BoardHeight { get; }

    /// <summary>Where the rectangle was taken (its top-left point).</summary>
    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stones { get; }

    public Stone this[int x, int y] => _cells[(y * Width) + x];

    /// <summary>The rectangle from (x0, y0) to (x1, y1), both included, of a position with at least one stone in it.</summary>
    public static BoardPattern FromRegion(BoardState board, int x0, int y0, int x1, int y1)
    {
        ArgumentNullException.ThrowIfNull(board);
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        if (x0 < 0 || y0 < 0 || x1 >= board.Width || y1 >= board.Height)
        {
            throw new ArgumentException("The region must be on the board.", nameof(board));
        }

        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var cells = new Stone[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                cells[(y * w) + x] = board[x0 + x, y0 + y];
            }
        }

        var pattern = new BoardPattern(board.Width, board.Height, x0, y0, w, h, cells);
        return pattern.Stones > 0 ? pattern : throw new ArgumentException("A pattern needs at least one stone.", nameof(board));
    }

    /// <summary>The pattern in every symmetry of the board and with both colour orders, without repeats.</summary>
    public IReadOnlyList<BoardPattern> Variants()
    {
        var variants = new List<BoardPattern>();
        var seen = new HashSet<string>();
        bool square = BoardWidth == BoardHeight;
        for (int t = 0; t < 8; t++)
        {
            bool transpose = t >= 4;
            if (transpose && !square)
            {
                continue;
            }

            bool flipX = (t & 1) != 0, flipY = (t & 2) != 0;
            for (int swap = 0; swap < 2; swap++)
            {
                BoardPattern v = Transform(transpose, flipX, flipY, swap == 1);
                if (seen.Add(v.Signature()))
                {
                    variants.Add(v);
                }
            }
        }

        return variants;
    }

    /// <summary>Where (the top-left point of the rectangle) the pattern is on a board, in any variant; null when it is not.</summary>
    public Point? FindIn(BoardState board) => FindIn(board, Variants());

    /// <summary>As <see cref="FindIn(BoardState)"/> with the variants worked out once (searching many positions).</summary>
    public static Point? FindIn(BoardState board, IReadOnlyList<BoardPattern> variants)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(variants);
        foreach (BoardPattern v in variants)
        {
            if (v.BoardWidth != board.Width || v.BoardHeight != board.Height)
            {
                continue;
            }

            (int fromX, int toX) = v._left ? (0, 0) : v._right ? (board.Width - v.Width, board.Width - v.Width) : (0, board.Width - v.Width);
            (int fromY, int toY) = v._top ? (0, 0) : v._bottom ? (board.Height - v.Height, board.Height - v.Height) : (0, board.Height - v.Height);
            for (int oy = fromY; oy <= toY; oy++)
            {
                for (int ox = fromX; ox <= toX; ox++)
                {
                    if (v.MatchesAt(board, ox, oy))
                    {
                        return new Point(ox, oy);
                    }
                }
            }
        }

        return null;
    }

    private bool MatchesAt(BoardState board, int ox, int oy)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (board[ox + x, oy + y] != _cells[(y * Width) + x])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private BoardPattern Transform(bool transpose, bool flipX, bool flipY, bool swap)
    {
        int bw = BoardWidth, bh = BoardHeight;
        (int Tx, int Ty) Map(int x, int y)
        {
            if (flipX)
            {
                x = bw - 1 - x;
            }

            if (flipY)
            {
                y = bh - 1 - y;
            }

            return transpose ? (y, x) : (x, y);
        }

        (int ax, int ay) = Map(X, Y);
        (int bx, int by) = Map(X + Width - 1, Y + Height - 1);
        int x0 = Math.Min(ax, bx), y0 = Math.Min(ay, by);
        int w = Math.Abs(bx - ax) + 1, h = Math.Abs(by - ay) + 1;
        var cells = new Stone[w * h];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                (int tx, int ty) = Map(X + x, Y + y);
                Stone c = _cells[(y * Width) + x];
                cells[((ty - y0) * w) + (tx - x0)] = swap ? c.Opponent() : c;
            }
        }

        return new BoardPattern(transpose ? bh : bw, transpose ? bw : bh, x0, y0, w, h, cells);
    }

    private string Signature()
    {
        // Same cells and the same anchoring: the same search. Where it was taken matters only on an edge.
        string place = $"{(_left ? X : -1)},{(_top ? Y : -1)},{(_right ? 1 : 0)},{(_bottom ? 1 : 0)}";
        return $"{Width}x{Height}@{place}:{string.Concat(_cells.Select(c => c switch { Stone.Black => 'b', Stone.White => 'w', _ => '.' }))}";
    }
}
