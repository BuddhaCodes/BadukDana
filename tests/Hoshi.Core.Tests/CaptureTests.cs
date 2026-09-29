using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class CaptureTests
{
    [Fact]
    public void Single_stone_is_captured_in_the_centre()
    {
        BoardState board = Parse(
            ". . . . .",
            ". . X . .",
            ". X O X .",
            ". . . . .",
            ". . . . .");

        MoveResult result = board.TryPlay(Stone.Black, P(2, 3));

        result.IsLegal.Should().BeTrue();
        result.Captured.Should().Equal(P(2, 2));
        result.State![P(2, 2)].Should().Be(Stone.Empty);
        result.State.BlackCaptures.Should().Be(1);
        result.State.WhiteCaptures.Should().Be(0);
    }

    [Fact]
    public void Stone_is_captured_in_the_corner()
    {
        BoardState board = Parse(
            "O X .",
            ". . .",
            ". . .");

        BoardState after = board.Play(Stone.Black, P(0, 1));

        after[P(0, 0)].Should().Be(Stone.Empty);
        after.BlackCaptures.Should().Be(1);
    }

    [Fact]
    public void Group_on_the_edge_is_captured()
    {
        BoardState board = Parse(
            "X O O . .",
            ". X X . .",
            ". . . . .");

        MoveResult result = board.TryPlay(Stone.Black, P(3, 0));

        result.Captured.Should().BeEquivalentTo([P(1, 0), P(2, 0)]);
        result.State!.BlackCaptures.Should().Be(2);
    }

    [Fact]
    public void Two_stone_group_is_captured_by_the_last_liberty()
    {
        BoardState board = Parse(
            ". X X . .",
            "X O O . .",
            ". X X . .",
            ". . . . .");

        MoveResult result = board.TryPlay(Stone.Black, P(3, 1));

        result.Captured.Should().BeEquivalentTo([P(1, 1), P(2, 1)]);
        result.State!.BlackCaptures.Should().Be(2);
    }

    [Fact]
    public void One_move_can_capture_several_groups_at_once()
    {
        BoardState board = Parse(
            ". X . X .",
            "X O . O X",
            ". X . X .",
            ". . . . .");

        MoveResult result = board.TryPlay(Stone.Black, P(2, 1));

        result.Captured.Should().BeEquivalentTo([P(1, 1), P(3, 1)]);
        result.State!.BlackCaptures.Should().Be(2);
        Render(result.State).Should().Be(string.Join('\n',
            ".X.X.",
            "X.X.X",
            ".X.X.",
            "....."));
    }

    [Fact]
    public void Capturing_takes_precedence_over_suicide()
    {
        // Black at (0,0) has no liberties of its own, but it removes the last liberty of the white stone.
        BoardState board = Parse(
            ". O X .",
            "O X . .",
            "X . . .");

        MoveResult result = board.TryPlay(Stone.Black, P(0, 0));

        result.IsLegal.Should().BeTrue();
        result.Captured.Should().BeEquivalentTo([P(1, 0), P(0, 1)]);
    }

    [Fact]
    public void Snapback_captures_the_capturing_group()
    {
        BoardState board = Parse(
            ". . X O . . .",
            "X X X O . . .",
            "O O O . . . .",
            ". . . . . . .");

        // White throws in at (1,0); Black captures it at (0,0); White plays (1,0) again and captures five.
        BoardState afterThrowIn = board.Play(Stone.White, P(1, 0));
        afterThrowIn.CountLiberties(P(1, 0)).Should().Be(1);

        MoveResult blackTakes = afterThrowIn.TryPlay(Stone.Black, P(0, 0));
        blackTakes.Captured.Should().Equal(P(1, 0));
        blackTakes.State!.KoPoint.Should().BeNull("the capturing stone joined a larger group, so this is not a ko");

        MoveResult snapback = blackTakes.State.TryPlay(Stone.White, P(1, 0));

        snapback.IsLegal.Should().BeTrue();
        snapback.Captured.Should().HaveCount(5);
        snapback.State!.WhiteCaptures.Should().Be(5);
        snapback.State.BlackCaptures.Should().Be(1);
    }

    [Fact]
    public void Capture_counts_are_tracked_per_color()
    {
        BoardState board = Parse(
            ". X . . . O .",
            "X O . . O X .",
            ". X . . . O .");

        BoardState after = board.Play(Stone.Black, P(2, 1)).Play(Stone.White, P(6, 1));

        after.BlackCaptures.Should().Be(1);
        after.WhiteCaptures.Should().Be(1);
        after.CapturesBy(Stone.Black).Should().Be(1);
        after[P(1, 1)].Should().Be(Stone.Empty);
        after[P(5, 1)].Should().Be(Stone.Empty);
    }
}
