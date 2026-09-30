using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class TerritoryTests
{
    [Fact]
    public void Empty_board_has_no_territory_and_komi_decides()
    {
        TerritoryEstimate e = TerritoryEstimator.Estimate(BoardState.Create(9), komi: 6.5);

        e.BlackSecure.Should().Be(0);
        e.WhiteSecure.Should().Be(0);
        e.OwnershipAt(new Point(4, 4)).Should().Be(0);
        e.Lead.Should().Be(-6.5, "only komi counts on an empty board");
    }

    [Fact]
    public void Regions_enclosed_by_one_colour_are_secure_territory()
    {
        BoardState b = Parse(
            ". . X . . . O . .",
            ". . X . . . O . .",
            "X X X . . . O O O",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .");

        TerritoryEstimate e = TerritoryEstimator.Estimate(b, komi: 0);

        e.SecureOwner(new Point(0, 0)).Should().Be(Stone.Black);
        e.SecureOwner(new Point(1, 1)).Should().Be(Stone.Black);
        e.SecureOwner(new Point(8, 0)).Should().Be(Stone.White);
        e.BlackSecure.Should().Be(4);
        e.WhiteSecure.Should().Be(4);
        e.SecureOwner(new Point(4, 5)).Should().Be(Stone.Empty, "the open centre touches both colours");
        e.OwnershipAt(new Point(0, 0)).Should().Be(1);
        e.OwnershipAt(new Point(8, 1)).Should().Be(-1);
    }

    [Fact]
    public void Stones_project_influence_that_fades_with_distance()
    {
        BoardState b = Parse(
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . X . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . O . .",
            ". . . . . . . . .",
            ". . . . . . . . .");

        TerritoryEstimate e = TerritoryEstimator.Estimate(b, komi: 0);

        e.OwnershipAt(new Point(1, 1)).Should().BeGreaterThan(0, "near black");
        e.OwnershipAt(new Point(7, 7)).Should().BeLessThan(0, "near white");
        e.OwnershipAt(new Point(2, 3)).Should().BeGreaterThan(e.OwnershipAt(new Point(2, 5)), "influence fades");
        Math.Abs(e.OwnershipAt(new Point(4, 4))).Should().BeLessThan(0.15, "the midpoint is contested");
        e.BlackPotential.Should().BeApproximately(e.WhitePotential, 0.5, "the position is symmetric");
        e.BlackSecure.Should().Be(0, "nothing is enclosed yet");
    }

    [Fact]
    public void Walls_claim_more_potential_than_single_stones()
    {
        BoardState wall = Parse(
            ". . . . . . . . .",
            ". . . . . . . . .",
            "X X X X X X X X X",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . . . .",
            ". . . . . . O . .",
            ". . . . . . . . .");

        TerritoryEstimate e = TerritoryEstimator.Estimate(wall, komi: 6.5);

        e.BlackSecure.Should().Be(18, "the two rows above the wall are enclosed");
        e.BlackPotential.Should().BeGreaterThan(e.WhitePotential);
        e.Lead.Should().BeGreaterThan(0, "black is clearly ahead");
    }

    [Fact]
    public void Lead_counts_captures_and_komi()
    {
        BoardState b = BoardState.Create(9).Setup([(new Point(0, 0), Stone.Black), (new Point(1, 0), Stone.White), (new Point(0, 1), Stone.White)]);
        BoardState captured = b.Play(Stone.White, new Point(8, 8)).Pass(Stone.Black);

        TerritoryEstimate plain = TerritoryEstimator.Estimate(captured, komi: 0);
        TerritoryEstimate withKomi = TerritoryEstimator.Estimate(captured, komi: 7);

        (plain.Lead - withKomi.Lead).Should().Be(7);
    }

    [Fact]
    public void An_external_ownership_map_replaces_the_heuristic()
    {
        BoardState b = BoardState.Create(3);
        double[] ownership = [1, 1, 0.9, 0.2, 0, -0.2, -0.9, -1, -1];

        TerritoryEstimate e = TerritoryEstimate.FromOwnership(b, ownership, komi: 0.5);

        e.OwnershipAt(new Point(2, 0)).Should().Be(0.9);
        e.SecureOwner(new Point(0, 0)).Should().Be(Stone.Black);
        e.SecureOwner(new Point(1, 1)).Should().Be(Stone.Empty);
        e.BlackSecure.Should().Be(3);
        e.WhiteSecure.Should().Be(3);
        e.Lead.Should().BeApproximately(-0.5, 0.001);
    }
}
