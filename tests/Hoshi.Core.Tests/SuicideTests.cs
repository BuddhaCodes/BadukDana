using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class SuicideTests
{
    private static readonly string[] SingleStoneSuicide =
    [
        ". X .",
        "X . .",
        ". . .",
    ];

    private static readonly string[] MultiStoneSuicide =
    [
        ". O X",
        "O X .",
        "X . .",
    ];

    [Fact]
    public void Single_stone_suicide_is_illegal_under_japanese_rules()
    {
        MoveResult result = Parse(RuleSet.Japanese, SingleStoneSuicide).TryPlay(Stone.White, P(0, 0));

        result.IsLegal.Should().BeFalse();
        result.Reason.Should().Be(IllegalMoveReason.Suicide);
    }

    [Fact]
    public void Multi_stone_suicide_is_illegal_under_chinese_rules()
    {
        Parse(RuleSet.Chinese, MultiStoneSuicide).TryPlay(Stone.White, P(0, 0))
            .Reason.Should().Be(IllegalMoveReason.Suicide);
    }

    [Fact]
    public void Multi_stone_suicide_is_legal_under_new_zealand_rules_and_removes_the_group()
    {
        MoveResult result = Parse(RuleSet.NewZealand, MultiStoneSuicide).TryPlay(Stone.White, P(0, 0));

        result.IsLegal.Should().BeTrue();
        result.Captured.Should().BeEquivalentTo([P(0, 0), P(1, 0), P(0, 1)]);
        result.State![P(0, 0)].Should().Be(Stone.Empty);
        result.State[P(1, 0)].Should().Be(Stone.Empty);
        result.State.BlackCaptures.Should().Be(3, "the suicided stones count as prisoners for the opponent");
    }

    [Fact]
    public void Filling_an_own_eye_that_still_has_outside_liberties_is_not_suicide()
    {
        BoardState board = Parse(
            ". X . .",
            "X . . .",
            ". . . .");

        board.TryPlay(Stone.Black, P(0, 0)).IsLegal.Should().BeTrue();
    }
}
