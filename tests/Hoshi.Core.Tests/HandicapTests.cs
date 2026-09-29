using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class HandicapTests
{
    // 19×19 star points.
    private static readonly Point TopRight = new(15, 3);
    private static readonly Point BottomLeft = new(3, 15);
    private static readonly Point BottomRight = new(15, 15);
    private static readonly Point TopLeft = new(3, 3);
    private static readonly Point Centre = new(9, 9);
    private static readonly Point LeftSide = new(3, 9);
    private static readonly Point RightSide = new(15, 9);
    private static readonly Point TopSide = new(9, 3);
    private static readonly Point BottomSide = new(9, 15);

    public static TheoryData<int, Point[]> Fixed19 => new()
    {
        { 2, [TopRight, BottomLeft] },
        { 3, [TopRight, BottomLeft, BottomRight] },
        { 4, [TopRight, BottomLeft, BottomRight, TopLeft] },
        { 5, [TopRight, BottomLeft, BottomRight, TopLeft, Centre] },
        { 6, [TopRight, BottomLeft, BottomRight, TopLeft, LeftSide, RightSide] },
        { 7, [TopRight, BottomLeft, BottomRight, TopLeft, LeftSide, RightSide, Centre] },
        { 8, [TopRight, BottomLeft, BottomRight, TopLeft, LeftSide, RightSide, TopSide, BottomSide] },
        { 9, [TopRight, BottomLeft, BottomRight, TopLeft, LeftSide, RightSide, TopSide, BottomSide, Centre] },
    };

    [Theory]
    [MemberData(nameof(Fixed19))]
    public void Fixed_handicap_on_19x19_uses_the_traditional_star_points(int stones, Point[] expected)
    {
        Handicap.FixedPoints(19, stones).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Fixed_handicap_on_13x13_and_9x9()
    {
        Handicap.FixedPoints(13, 5).Should().BeEquivalentTo([P(9, 3), P(3, 9), P(9, 9), P(3, 3), P(6, 6)]);
        Handicap.FixedPoints(9, 4).Should().BeEquivalentTo([P(6, 2), P(2, 6), P(6, 6), P(2, 2)]);
    }

    [Theory]
    [InlineData(19, 9)]
    [InlineData(13, 9)]
    [InlineData(9, 9)]
    [InlineData(10, 4)]
    [InlineData(7, 9)]
    [InlineData(5, 0)]
    public void Maximum_fixed_handicap_depends_on_size(int size, int max)
    {
        Handicap.MaxFixed(size).Should().Be(max);
    }

    [Theory]
    [InlineData(19, 1)]
    [InlineData(19, 10)]
    [InlineData(10, 5)]
    public void Unsupported_fixed_handicaps_throw(int size, int stones)
    {
        FluentActions.Invoking(() => Handicap.FixedPoints(size, stones))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fixed_handicap_places_black_stones_and_gives_white_the_first_move()
    {
        BoardState board = BoardState.Create(19).WithFixedHandicap(4);

        board.ToMove.Should().Be(Stone.White);
        new[] { TopRight, BottomLeft, BottomRight, TopLeft }.Should().OnlyContain(p => board[p] == Stone.Black);
        board.AllPoints.Count(p => board[p] != Stone.Empty).Should().Be(4);
    }

    [Fact]
    public void Free_handicap_places_stones_where_requested()
    {
        BoardState board = BoardState.Create(19).WithHandicap([P(3, 3), P(10, 4), P(16, 16)]);

        board[P(10, 4)].Should().Be(Stone.Black);
        board.ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public void Free_handicap_rejects_duplicates_and_too_few_stones()
    {
        BoardState empty = BoardState.Create(9);

        FluentActions.Invoking(() => empty.WithHandicap([P(2, 2), P(2, 2)])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => empty.WithHandicap([P(2, 2)])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => empty.WithHandicap([P(2, 2), P(9, 9)])).Should().Throw<ArgumentException>();
    }
}
