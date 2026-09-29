namespace Hoshi.Core.Tests;

/// <summary>
/// Builds positions from ASCII diagrams. Rows go top to bottom (y = 0 first), columns left to right.
/// <c>X</c> = black, <c>O</c> = white, <c>.</c> = empty. Spaces are ignored.
/// </summary>
internal static class Boards
{
    public static BoardState Parse(params string[] rows) => Parse(RuleSet.Japanese, rows);

    public static BoardState Parse(RuleSet rules, params string[] rows)
    {
        string[] clean = rows.Select(r => r.Replace(" ", string.Empty, StringComparison.Ordinal)).ToArray();
        int width = clean[0].Length;
        int height = clean.Length;
        if (clean.Any(r => r.Length != width))
        {
            throw new ArgumentException("All rows must have the same width.", nameof(rows));
        }

        var stones = new List<(Point, Stone)>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Stone s = clean[y][x] switch
                {
                    'X' => Stone.Black,
                    'O' => Stone.White,
                    '.' => Stone.Empty,
                    char c => throw new ArgumentException($"Unexpected character '{c}'.", nameof(rows)),
                };
                if (s != Stone.Empty)
                {
                    stones.Add((new Point(x, y), s));
                }
            }
        }

        return BoardState.Create(width, height, rules).Setup(stones);
    }

    /// <summary>Renders the board back to the same ASCII format (rows joined by '\n').</summary>
    public static string Render(BoardState board)
    {
        var lines = new List<string>();
        for (int y = 0; y < board.Height; y++)
        {
            var chars = new char[board.Width];
            for (int x = 0; x < board.Width; x++)
            {
                chars[x] = board[x, y] switch
                {
                    Stone.Black => 'X',
                    Stone.White => 'O',
                    _ => '.',
                };
            }

            lines.Add(new string(chars));
        }

        return string.Join('\n', lines);
    }

    public static Point P(int x, int y) => new(x, y);
}
