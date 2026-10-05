using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class GroupStrengthTests
{
    [Fact]
    public void Without_an_engine_only_short_liberties_count_and_quiet_stones_are_left_alone()
    {
        BoardState board = Parse(
            ". X O O .",
            ". X O X .",
            ". . X . .",
            ". . . . .",
            "X . . O .");

        IReadOnlyList<GroupStatus> groups = GroupStrength.Assess(board);

        groups.Should().ContainSingle(g => g.Color == Stone.White && g.Stones.Count == 3)
            .Which.Health.Should().Be(GroupHealth.Critical, "one liberty left");
        groups.Should().ContainSingle(g => g.Color == Stone.Black && g.Stones.Contains(P(3, 1)))
            .Which.Health.Should().Be(GroupHealth.Weak, "two liberties, in contact");
        groups.Should().NotContain(g => g.Stones.Contains(P(0, 4)), "a lone corner stone with two liberties and no enemy near is not a fight");
        groups.Should().NotContain(g => g.Stones.Contains(P(3, 4)));
        groups.Should().NotContain(g => g.Health == GroupHealth.Strong, "strong groups are not reported");
    }

    [Fact]
    public void With_ownership_a_group_is_as_healthy_as_the_engine_thinks()
    {
        BoardState board = Parse(
            "X . . . .",
            ". . . . .",
            ". . O . .",
            ". . . . .",
            ". . . . X");
        // Ownership from Black's view, row-major: the corner black stone is solid, the centre white stone is
        // contested, the bottom-right black stone is probably lost.
        double[] own = new double[25];
        own[0] = 0.95;
        own[(2 * 5) + 2] = -0.1;
        own[(4 * 5) + 4] = -0.7;

        IReadOnlyList<GroupStatus> groups = GroupStrength.Assess(board, own);

        groups.Should().NotContain(g => g.Stones.Contains(P(0, 0)));
        groups.Should().ContainSingle(g => g.Stones.Contains(P(2, 2))).Which.Health.Should().Be(GroupHealth.Weak);
        groups.Should().ContainSingle(g => g.Stones.Contains(P(4, 4))).Which.Health.Should().Be(GroupHealth.Critical);
    }

    [Fact]
    public void Ownership_of_a_group_is_averaged_and_unsettled_only_counts_when_the_enemy_is_near()
    {
        BoardState board = Parse(
            ". . . . .",
            ". X X . .",
            ". . . . .",
            ". . . . O",
            ". . . . .");
        double[] own = new double[25];
        own[6] = 0.5;
        own[7] = 0.3;
        own[19] = -0.9;

        GroupStatus g = GroupStrength.Assess(board, own).Should().ContainSingle().Subject;
        g.Health.Should().Be(GroupHealth.Unsettled, "White at E2 is within two points");
        g.Score.Should().BeApproximately(0.4, 1e-9);

        BoardState lonely = Parse(
            ". . . . . .",
            ". X X . . .",
            ". . . . . .",
            ". . . . . .",
            ". . . . . O");
        double[] own2 = new double[30];
        own2[7] = 0.5;
        own2[8] = 0.3;
        own2[29] = -0.9;
        GroupStrength.Assess(lonely, own2).Should().BeEmpty("an opening stone on its own is not a worry");
    }

    [Fact]
    public void An_atari_the_engine_calls_safe_is_still_at_least_weak()
    {
        BoardState board = Parse(
            ". X O .",
            ". O . .",
            ". . . .",
            ". . . .");
        double[] own = new double[16];
        own[1] = 0.9; // a snapback, say: the engine is not worried

        GroupStrength.Assess(board, own).Should().ContainSingle(g => g.Color == Stone.Black)
            .Which.Health.Should().Be(GroupHealth.Weak);
    }
}
