namespace Hoshi.Core.Tests;

public sealed class BoardPatternTests
{
    private static BoardState Board(int size, params (int X, int Y, Stone C)[] stones) =>
        BoardState.Create(size).Setup(stones.Select(s => (new Point(s.X, s.Y), s.C)));

    [Fact]
    public void A_corner_pattern_is_found_in_every_corner_with_either_colours()
    {
        // 3-4 point approach in the top-left corner.
        BoardState source = Board(19, (3, 2, Stone.Black), (2, 4, Stone.White));
        BoardPattern pattern = BoardPattern.FromRegion(source, 0, 0, 6, 6);
        pattern.Stones.Should().Be(2);

        pattern.FindIn(source).Should().NotBeNull();
        pattern.FindIn(Board(19, (15, 2, Stone.Black), (16, 4, Stone.White))).Should().NotBeNull("mirrored to the top right");
        pattern.FindIn(Board(19, (2, 15, Stone.White), (4, 16, Stone.Black))).Should().NotBeNull("transposed to the bottom left, colours swapped");
        pattern.FindIn(Board(19, (3, 2, Stone.Black), (2, 4, Stone.White), (5, 5, Stone.Black))).Should().BeNull("an extra stone in the region");
        pattern.FindIn(Board(19, (4, 3, Stone.Black), (3, 5, Stone.White))).Should().BeNull("a corner pattern stays on the edges");
    }

    [Fact]
    public void A_centre_pattern_is_found_anywhere_away_from_the_edges()
    {
        BoardState source = Board(19, (9, 9, Stone.Black), (10, 9, Stone.White), (9, 10, Stone.White));
        BoardPattern pattern = BoardPattern.FromRegion(source, 8, 8, 11, 11);

        pattern.FindIn(Board(19, (4, 13, Stone.Black), (5, 13, Stone.White), (4, 14, Stone.White))).Should().Be(new Point(3, 12));
        pattern.FindIn(Board(19, (4, 13, Stone.White), (4, 12, Stone.Black), (3, 13, Stone.Black))).Should().NotBeNull("rotated and colours swapped");
        pattern.FindIn(Board(19, (4, 13, Stone.Black))).Should().BeNull();
    }

    [Fact]
    public void Patterns_need_the_same_board_size_and_some_stones()
    {
        BoardState source = Board(9, (2, 2, Stone.Black));
        BoardPattern pattern = BoardPattern.FromRegion(source, 0, 0, 4, 4);
        pattern.FindIn(Board(19, (2, 2, Stone.Black))).Should().BeNull();
        FluentActions.Invoking(() => BoardPattern.FromRegion(BoardState.Create(9), 0, 0, 3, 3)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BoardPattern.FromRegion(source, 3, 3, 12, 4)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Variants_drop_the_symmetric_duplicates()
    {
        // A single stone on tengen looks the same under every symmetry: one variant per colour.
        BoardPattern pattern = BoardPattern.FromRegion(Board(19, (9, 9, Stone.Black)), 9, 9, 9, 9);
        pattern.Variants().Should().HaveCount(2);
    }
}
