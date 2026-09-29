using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class BoardBasicsTests
{
    [Fact]
    public void New_board_is_empty_with_black_to_move()
    {
        BoardState board = BoardState.Create(19);

        board.Width.Should().Be(19);
        board.Height.Should().Be(19);
        board.ToMove.Should().Be(Stone.Black);
        board.KoPoint.Should().BeNull();
        board.BlackCaptures.Should().Be(0);
        board.WhiteCaptures.Should().Be(0);
        board.AllPoints.Should().HaveCount(361).And.OnlyContain(p => board[p] == Stone.Empty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(26)]
    public void Unsupported_sizes_are_rejected(int size)
    {
        FluentActions.Invoking(() => BoardState.Create(size)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rectangular_boards_are_supported()
    {
        BoardState board = BoardState.Create(9, 13);

        board.Width.Should().Be(9);
        board.Height.Should().Be(13);
        board.IsOnBoard(P(8, 12)).Should().BeTrue();
        board.IsOnBoard(P(9, 0)).Should().BeFalse();
    }

    [Fact]
    public void Playing_returns_a_new_state_and_leaves_the_original_untouched()
    {
        BoardState before = BoardState.Create(9);

        BoardState after = before.Play(Stone.Black, P(4, 4));

        after.Should().NotBeSameAs(before);
        after[P(4, 4)].Should().Be(Stone.Black);
        after.ToMove.Should().Be(Stone.White);
        before[P(4, 4)].Should().Be(Stone.Empty);
        before.ToMove.Should().Be(Stone.Black);
    }

    [Fact]
    public void Playing_on_an_occupied_point_is_illegal()
    {
        BoardState board = BoardState.Create(9).Play(Stone.Black, P(2, 2));

        MoveResult result = board.TryPlay(Stone.White, P(2, 2));

        result.IsLegal.Should().BeFalse();
        result.Reason.Should().Be(IllegalMoveReason.Occupied);
        result.State.Should().BeNull();
    }

    [Fact]
    public void Playing_off_the_board_is_illegal()
    {
        BoardState.Create(9).TryPlay(Stone.Black, P(9, 0)).Reason.Should().Be(IllegalMoveReason.OutOfBounds);
    }

    [Fact]
    public void Playing_empty_color_throws()
    {
        FluentActions.Invoking(() => BoardState.Create(9).TryPlay(Stone.Empty, P(0, 0)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Play_throws_on_illegal_move()
    {
        BoardState board = BoardState.Create(9).Play(Stone.Black, P(0, 0));

        FluentActions.Invoking(() => board.Play(Stone.White, P(0, 0)))
            .Should().Throw<IllegalMoveException>().Which.Reason.Should().Be(IllegalMoveReason.Occupied);
    }

    [Fact]
    public void Colors_may_be_played_out_of_turn_as_SGF_allows()
    {
        BoardState board = BoardState.Create(9).Play(Stone.Black, P(0, 0)).Play(Stone.Black, P(1, 1));

        board[P(1, 1)].Should().Be(Stone.Black);
        board.ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public void Pass_switches_player_and_keeps_stones()
    {
        BoardState board = BoardState.Create(9).Play(Stone.Black, P(4, 4));

        BoardState passed = board.Pass(Stone.White);

        passed.ToMove.Should().Be(Stone.Black);
        passed[P(4, 4)].Should().Be(Stone.Black);
        passed.Hash.Should().Be(board.Hash);
    }

    [Fact]
    public void Groups_and_liberties_are_reported()
    {
        BoardState board = Parse(
            ". . . . .",
            ". X X . .",
            ". X O . .",
            ". . . . .",
            ". . . . .");

        board.GetGroup(P(1, 1)).Should().BeEquivalentTo([P(1, 1), P(2, 1), P(1, 2)]);
        board.CountLiberties(P(1, 1)).Should().Be(6);
        board.CountLiberties(P(2, 2)).Should().Be(2);
        board.GetGroup(P(0, 0)).Should().BeEmpty();
    }

    [Fact]
    public void Opponent_of_each_color()
    {
        Stone.Black.Opponent().Should().Be(Stone.White);
        Stone.White.Opponent().Should().Be(Stone.Black);
        Stone.Empty.Opponent().Should().Be(Stone.Empty);
    }
}
