namespace Hoshi.Core.Tests;

public sealed class PointTests
{
    [Theory]
    [InlineData(0, 0, "aa")]
    [InlineData(3, 3, "dd")]
    [InlineData(15, 3, "pd")]
    [InlineData(18, 18, "ss")]
    [InlineData(24, 24, "yy")]
    public void Sgf_round_trip(int x, int y, string sgf)
    {
        var p = new Point(x, y);

        p.ToSgf().Should().Be(sgf);
        Point.FromSgf(sgf).Should().Be(p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("A1")]
    [InlineData("zz")]
    public void Invalid_sgf_coordinates_are_rejected(string sgf)
    {
        Point.TryParseSgf(sgf, out _).Should().BeFalse();
        FluentActions.Invoking(() => Point.FromSgf(sgf)).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("", 19, true)]
    [InlineData("tt", 19, true)]
    [InlineData("tt", 13, true)]
    [InlineData("tt", 21, false)]
    [InlineData("dd", 19, false)]
    public void Sgf_pass_detection(string value, int boardSize, bool isPass)
    {
        Point.IsSgfPass(value, boardSize).Should().Be(isPass);
    }

    [Theory]
    [InlineData(0, 18, 19, "A1")]
    [InlineData(3, 3, 19, "D16")]
    [InlineData(7, 0, 19, "H19")]
    [InlineData(8, 0, 19, "J19")] // No letter I.
    [InlineData(18, 0, 19, "T19")]
    [InlineData(4, 4, 9, "E5")]
    [InlineData(24, 0, 25, "Z25")]
    public void Human_notation_round_trip(int x, int y, int height, string human)
    {
        var p = new Point(x, y);

        p.ToHuman(height).Should().Be(human);
        Point.FromHuman(human, height).Should().Be(p);
    }

    [Theory]
    [InlineData("d16", 19, 3, 3)]
    [InlineData(" q4 ", 19, 15, 15)]
    public void Human_notation_is_case_and_whitespace_insensitive(string human, int height, int x, int y)
    {
        Point.FromHuman(human, height).Should().Be(new Point(x, y));
    }

    [Theory]
    [InlineData("I5", 19)]
    [InlineData("A0", 19)]
    [InlineData("A20", 19)]
    [InlineData("5", 19)]
    [InlineData("", 19)]
    public void Invalid_human_coordinates_are_rejected(string human, int height)
    {
        Point.TryParseHuman(human, height, out _).Should().BeFalse();
    }
}
