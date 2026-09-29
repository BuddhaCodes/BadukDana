using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class ZobristTests
{
    [Fact]
    public void Same_position_via_different_move_orders_has_the_same_hash()
    {
        BoardState a = BoardState.Create(9)
            .Play(Stone.Black, P(2, 2)).Play(Stone.White, P(6, 6)).Play(Stone.Black, P(2, 6));
        BoardState b = BoardState.Create(9)
            .Play(Stone.Black, P(2, 6)).Play(Stone.White, P(6, 6)).Play(Stone.Black, P(2, 2));

        a.Hash.Should().Be(b.Hash);
    }

    [Fact]
    public void Played_and_set_up_positions_hash_equally()
    {
        BoardState played = BoardState.Create(9).Play(Stone.Black, P(3, 3)).Play(Stone.White, P(5, 5));
        BoardState setUp = BoardState.Create(9).Setup([(P(3, 3), Stone.Black), (P(5, 5), Stone.White)]);

        played.Hash.Should().Be(setUp.Hash);
    }

    [Fact]
    public void Color_matters()
    {
        BoardState black = BoardState.Create(9).Play(Stone.Black, P(4, 4));
        BoardState white = BoardState.Create(9).Play(Stone.White, P(4, 4));

        black.Hash.Should().NotBe(white.Hash);
        black.Hash.Should().NotBe(BoardState.Create(9).Hash);
    }

    [Fact]
    public void Capture_updates_the_hash()
    {
        BoardState board = Parse(
            ". X .",
            "X O .",
            ". X .");

        BoardState after = board.Play(Stone.Black, P(2, 1));
        BoardState expected = Parse(
            ". X .",
            "X . X",
            ". X .");

        after.Hash.Should().Be(expected.Hash);
    }

    [Fact]
    public void Hashes_are_deterministic_across_instances()
    {
        BoardState.Create(19).Play(Stone.Black, P(15, 3)).Hash
            .Should().Be(BoardState.Create(19).Play(Stone.Black, P(15, 3)).Hash);
    }
}
