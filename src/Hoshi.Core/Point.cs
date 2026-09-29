using System.Globalization;

namespace Hoshi.Core;

/// <summary>
/// A board intersection. Origin (0,0) is the top-left corner; X grows to the right, Y grows downwards.
/// </summary>
public readonly record struct Point(int X, int Y)
{
    /// <summary>Column letters used in human notation (no "I").</summary>
    public const string HumanColumns = "ABCDEFGHJKLMNOPQRSTUVWXYZ";

    private const string SgfLetters = "abcdefghijklmnopqrstuvwxy";

    /// <summary>SGF coordinate, e.g. (3,3) → "dd".</summary>
    public string ToSgf()
    {
        if ((uint)X >= SgfLetters.Length || (uint)Y >= SgfLetters.Length)
        {
            throw new InvalidOperationException($"{this} cannot be written as an SGF coordinate.");
        }

        return string.Create(2, this, static (span, p) =>
        {
            span[0] = SgfLetters[p.X];
            span[1] = SgfLetters[p.Y];
        });
    }

    public static Point FromSgf(string value) =>
        TryParseSgf(value, out Point p) ? p : throw new FormatException($"'{value}' is not an SGF point.");

    /// <summary>Parses a two-letter SGF point. Passes ("" / "tt") are not points; see <see cref="IsSgfPass"/>.</summary>
    public static bool TryParseSgf(string? value, out Point point)
    {
        point = default;
        if (value is not { Length: 2 })
        {
            return false;
        }

        int x = SgfLetters.IndexOf(value[0], StringComparison.Ordinal);
        int y = SgfLetters.IndexOf(value[1], StringComparison.Ordinal);
        if (x < 0 || y < 0)
        {
            return false;
        }

        point = new Point(x, y);
        return true;
    }

    /// <summary>
    /// True when an SGF move value represents a pass: the empty value, or "tt" on boards up to 19×19 (FF[3] convention).
    /// </summary>
    public static bool IsSgfPass(string? value, int boardSize) =>
        string.IsNullOrEmpty(value) || (value == "tt" && boardSize <= 19);

    /// <summary>Human notation, e.g. (3,3) on 19×19 → "D16". Rows are counted from the bottom.</summary>
    public string ToHuman(int boardHeight)
    {
        if ((uint)X >= HumanColumns.Length || Y < 0 || Y >= boardHeight)
        {
            throw new InvalidOperationException($"{this} cannot be written in human notation on height {boardHeight}.");
        }

        return HumanColumns[X] + (boardHeight - Y).ToString(CultureInfo.InvariantCulture);
    }

    public static Point FromHuman(string value, int boardHeight) =>
        TryParseHuman(value, boardHeight, out Point p) ? p : throw new FormatException($"'{value}' is not a board coordinate.");

    public static bool TryParseHuman(string? value, int boardHeight, out Point point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> s = value.AsSpan().Trim();
        if (s.Length < 2)
        {
            return false;
        }

        int x = HumanColumns.IndexOf(char.ToUpperInvariant(s[0]));
        if (x < 0
            || !int.TryParse(s[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int row)
            || row < 1
            || row > boardHeight)
        {
            return false;
        }

        point = new Point(x, boardHeight - row);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X},{Y})");
}
