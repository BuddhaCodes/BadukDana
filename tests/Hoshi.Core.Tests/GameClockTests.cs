namespace Hoshi.Core.Tests;

public sealed class GameClockTests
{
    private static readonly TimeSpan S = TimeSpan.FromSeconds(1);

    [Fact]
    public void Absolute_time_runs_out()
    {
        var clock = new GameClock(TimeControl.Absolute(TimeSpan.FromMinutes(1)));
        clock.Elapse(Stone.Black, 59 * S).Should().BeFalse();
        clock.Black.Main.Should().Be(1 * S);
        clock.White.Main.Should().Be(TimeSpan.FromMinutes(1), "only the player to move loses time");
        clock.Elapse(Stone.Black, 2 * S).Should().BeTrue();
        clock.Black.IsFlagged.Should().BeTrue();
        clock.Black.Main.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Byo_yomi_periods_reset_after_a_move_and_are_used_up_one_by_one()
    {
        var clock = new GameClock(TimeControl.ByoYomi(10 * S, periods: 3, periodTime: 30 * S));
        clock.Elapse(Stone.Black, 15 * S).Should().BeFalse();
        clock.Black.Main.Should().Be(TimeSpan.Zero);
        clock.Black.Periods.Should().Be(3);
        clock.Black.Period.Should().Be(25 * S, "5 seconds into the first period");

        clock.MoveDone(Stone.Black);
        clock.Black.Period.Should().Be(30 * S, "a move in time keeps the period");
        clock.Black.Periods.Should().Be(3);

        clock.Elapse(Stone.Black, 45 * S).Should().BeFalse();
        clock.Black.Periods.Should().Be(2, "a whole period was used");
        clock.Black.Period.Should().Be(15 * S);
        clock.Elapse(Stone.Black, 75 * S).Should().BeTrue();
        clock.Black.IsFlagged.Should().BeTrue();
    }

    [Fact]
    public void Fischer_adds_the_increment_after_each_move_up_to_the_cap()
    {
        var clock = new GameClock(TimeControl.Fischer(TimeSpan.FromMinutes(5), 5 * S, maxTime: TimeSpan.FromMinutes(5)));
        clock.Elapse(Stone.White, 2 * S);
        clock.MoveDone(Stone.White);
        clock.White.Main.Should().Be(TimeSpan.FromMinutes(5), "capped at the maximum");
        clock.Elapse(Stone.White, 20 * S);
        clock.MoveDone(Stone.White);
        clock.White.Main.Should().Be(TimeSpan.FromMinutes(5) - (15 * S));
    }

    [Fact]
    public void Clocks_are_described_for_people()
    {
        TimeControl.ByoYomi(TimeSpan.FromMinutes(10), 5, 30 * S).Describe().Should().Be("10:00 + 5×30s");
        TimeControl.Fischer(TimeSpan.FromMinutes(5), 5 * S).Describe().Should().Be("5:00 + 5s");
        TimeControl.Absolute(TimeSpan.FromMinutes(20)).Describe().Should().Be("20:00");
        GameClock.Format(TimeSpan.FromSeconds(3725)).Should().Be("1:02:05");
        GameClock.Format(TimeSpan.FromSeconds(65.4)).Should().Be("1:06", "seconds are rounded up while the clock runs");
    }
}
