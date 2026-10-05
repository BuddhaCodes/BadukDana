using Hoshi.Core;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Tests;

public sealed class MoveImportanceTests
{
    private static TurnAnalysis Analysis(Stone toMove, params double[] leads) => new(
        0, toMove, 0.5, leads[0], 500,
        [.. leads.Select((l, i) => new MoveCandidate(new Point(i, 0), i, 100 - i, 0.5, l, 0.1, []))],
        null);

    [Fact]
    public void Criticality_is_the_best_moves_lead_over_the_typical_alternative_for_the_mover()
    {
        MoveImportance.Criticality(Analysis(Stone.Black, 3.0, 2.6, 2.4, 2.2, 2.0, -9), Stone.Black).Should().BeApproximately(0.7, 1e-9);
        MoveImportance.Criticality(Analysis(Stone.White, -5, 2, 3, 1, 0), Stone.White).Should().BeApproximately(6.5, 1e-9, "White's best is −5 (5 for White)");
        MoveImportance.Criticality(Analysis(Stone.Black, 1), Stone.Black).Should().Be(0, "nothing to compare with");
    }

    [Theory]
    [InlineData(MoveQuality.Best, 0.5, 0.0, true, 0)]   // a 4-4 point in the opening: routine
    [InlineData(MoveQuality.Best, 0.8, 0.6, false, 0)]  // a quiet good move later on
    [InlineData(MoveQuality.Best, 6.0, 0.0, true, 2)]   // an opening move that really matters
    [InlineData(MoveQuality.Best, 4.0, 4.5, false, 3)]  // the move that wins the fight
    [InlineData(MoveQuality.Excellent, 9.0, 4.0, false, 2)]
    [InlineData(MoveQuality.Good, 9.0, 4.0, false, 1)]
    [InlineData(MoveQuality.Mistake, 9.0, 4.0, false, 0)]
    [InlineData(MoveQuality.Best, 1.0, 2.0, true, 1)]   // contact play in the opening: a small one
    public void Celebrations_follow_the_moment_and_never_exceed_the_quality(MoveQuality quality, double criticality, double fight, bool opening, int expected) =>
        MoveImportance.Strength(quality, criticality, fight, opening).Should().Be(expected);

    [Fact]
    public void The_opening_is_about_one_eighteenth_of_the_board()
    {
        MoveImportance.IsOpening(20, 19, 19).Should().BeTrue();
        MoveImportance.IsOpening(21, 19, 19).Should().BeFalse();
        MoveImportance.IsOpening(6, 9, 9).Should().BeTrue();
        MoveImportance.IsOpening(7, 9, 9).Should().BeFalse();
    }
}
