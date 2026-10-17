using System.Globalization;
using System.Text;
using Hoshi.Core;

namespace Hoshi.Sgf.Export;

/// <summary>
/// The board as text: Sensei's Library diagrams (<c>$$</c>, read by its wiki and many forums) and a plain code block
/// for chats such as Discord or Reddit.
/// </summary>
public static class TextDiagram
{
    /// <summary>
    /// A Sensei's Library diagram of the whole board: <c>X</c>/<c>O</c> stones, <c>,</c> star points; marks follow
    /// the stone under them (circle <c>B</c>/<c>W</c>/<c>C</c>, square <c>#</c>/<c>@</c>/<c>S</c>, triangle
    /// <c>Y</c>/<c>Q</c>/<c>T</c>, cross <c>Z</c>/<c>P</c>/<c>M</c>), one-letter labels on empty points. The last move
    /// is circled when it has no mark of its own.
    /// </summary>
    public static string Senseis(BoardState board, Point? lastMove, IReadOnlyList<Markup>? markers, bool coordinates = true, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        var marks = new Dictionary<Point, Markup>();
        foreach (Markup m in markers ?? [])
        {
            marks.TryAdd(m.Point, m);
        }

        if (lastMove is { } last && board.IsOnBoard(last) && !marks.ContainsKey(last))
        {
            marks[last] = new Markup(last, MarkupKind.Circle);
        }

        var stars = new HashSet<Point>(StarPoints.For(board.Width, board.Height));
        var text = new StringBuilder("$$");
        text.Append(board.ToMove == Stone.White ? 'W' : 'B');
        if (coordinates)
        {
            text.Append('c').Append(Math.Max(board.Width, board.Height).ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            text.Append(' ').Append(title.ReplaceLineEndings(" ").Trim());
        }

        string frame = "$$ +" + new string('-', (2 * board.Width) + 1) + "+";
        text.Append('\n').Append(frame);
        for (int y = 0; y < board.Height; y++)
        {
            text.Append("\n$$ |");
            for (int x = 0; x < board.Width; x++)
            {
                var p = new Point(x, y);
                text.Append(' ').Append(Cell(board[p], marks.GetValueOrDefault(p), stars.Contains(p)));
            }

            text.Append(" |");
        }

        text.Append('\n').Append(frame);
        return text.ToString();
    }

    /// <summary>A code block (```) with coordinates around it; the last move is shown as <c>[X]</c>.</summary>
    public static string CodeBlock(BoardState board, Point? lastMove)
    {
        ArgumentNullException.ThrowIfNull(board);
        var stars = new HashSet<Point>(StarPoints.For(board.Width, board.Height));
        string columns = "   " + string.Join(' ', Enumerable.Range(0, board.Width).Select(x => Point.HumanColumns[Math.Min(x, Point.HumanColumns.Length - 1)]));
        var text = new StringBuilder("```\n").Append(columns);
        for (int y = 0; y < board.Height; y++)
        {
            string label = (board.Height - y).ToString(CultureInfo.InvariantCulture).PadLeft(2);
            char[] row = (label + " " + string.Join(' ', Enumerable.Range(0, board.Width).Select(x =>
            {
                var p = new Point(x, y);
                return board[p] switch
                {
                    Stone.Black => 'X',
                    Stone.White => 'O',
                    _ => stars.Contains(p) ? '+' : '.',
                };
            })) + "  " + label.TrimStart()).ToCharArray();
            if (lastMove is { } last && last.Y == y && board.IsOnBoard(last))
            {
                int at = 3 + (2 * last.X);
                row[at - 1] = '[';
                row[at + 1] = ']';
            }

            text.Append('\n').Append(row);
        }

        return text.Append('\n').Append(columns).Append("\n```").ToString();
    }

    private static char Cell(Stone stone, Markup? mark, bool star) => (stone, mark?.Kind) switch
    {
        (Stone.Black, MarkupKind.Circle) => 'B',
        (Stone.White, MarkupKind.Circle) => 'W',
        (Stone.Empty, MarkupKind.Circle) => 'C',
        (Stone.Black, MarkupKind.Square) => '#',
        (Stone.White, MarkupKind.Square) => '@',
        (Stone.Empty, MarkupKind.Square) => 'S',
        (Stone.Black, MarkupKind.Triangle) => 'Y',
        (Stone.White, MarkupKind.Triangle) => 'Q',
        (Stone.Empty, MarkupKind.Triangle) => 'T',
        (Stone.Black, MarkupKind.Cross) => 'Z',
        (Stone.White, MarkupKind.Cross) => 'P',
        (Stone.Empty, MarkupKind.Cross) => 'M',
        (Stone.Empty, MarkupKind.Label) when mark!.Text is { Length: 1 } t && char.IsAsciiLetterLower(t[0]) => t[0],
        (Stone.Black, _) => 'X',
        (Stone.White, _) => 'O',
        _ => star ? ',' : '.',
    };
}
