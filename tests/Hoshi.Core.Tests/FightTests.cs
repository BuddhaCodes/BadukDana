using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class FightTests
{
    private static (BoardState After, FightReading Reading) Play(FightMeter meter, BoardState board, Stone color, Point p)
    {
        BoardState after = board.TryPlay(color, p).State!;
        return (after, meter.OnMove(board, after, p));
    }

    [Fact]
    public void A_quiet_opening_move_is_no_fight()
    {
        var meter = new FightMeter();
        (_, FightReading r) = Play(meter, BoardState.Create(19), Stone.Black, P(15, 3));

        r.Intensity.Should().Be(0);
        r.Strength.Should().Be(0);
    }

    [Fact]
    public void Contact_and_short_liberties_heat_up_a_local_fight()
    {
        var meter = new FightMeter();
        BoardState b = BoardState.Create(19);
        var readings = new List<FightReading>();
        foreach ((Stone c, Point p) in new[]
        {
            (Stone.Black, P(9, 9)), (Stone.White, P(10, 9)), (Stone.Black, P(10, 10)), (Stone.White, P(9, 10)),
            (Stone.Black, P(8, 10)), (Stone.White, P(9, 11)), (Stone.Black, P(11, 9)),
        })
        {
            (b, FightReading r) = Play(meter, b, c, p);
            readings.Add(r);
        }

        readings[^1].Heat.Should().BeGreaterThan(readings[1].Heat, "the battle builds while it stays in one place");
        readings.Skip(2).Should().Contain(r => r.Strength >= 1);
    }

    [Fact]
    public void A_big_capture_is_an_all_out_battle_and_tenuki_cools_it()
    {
        BoardState board = Parse(
            ". X X X .",
            "X O O O X",
            ". X X . .",
            ". . . . .",
            ". . . . .");
        var meter = new FightMeter();
        (BoardState after, FightReading capture) = Play(meter, board, Stone.Black, P(3, 2));

        capture.Strength.Should().Be(3, "three stones captured in a fight");
        (BoardState far, FightReading tenuki) = Play(meter, after, Stone.White, P(0, 4));
        tenuki.Heat.Should().BeLessThan(capture.Heat);
        far.Should().NotBeNull();
    }
}
