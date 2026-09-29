using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class KoTests
{
    private static BoardState KoPosition(RuleSet rules) => Parse(rules,
        ". X O . .",
        "X O . O .",
        ". X O . .",
        ". . . . .",
        ". . . . .");

    private static readonly Point BlackTakes = new(2, 1);
    private static readonly Point WhiteRetakes = new(1, 1);

    [Fact]
    public void Taking_a_ko_sets_the_ko_point()
    {
        MoveResult result = KoPosition(RuleSet.Japanese).TryPlay(Stone.Black, BlackTakes);

        result.Captured.Should().Equal(WhiteRetakes);
        result.State!.KoPoint.Should().Be(WhiteRetakes);
    }

    [Fact]
    public void Immediate_recapture_is_illegal()
    {
        BoardState afterTake = KoPosition(RuleSet.Japanese).Play(Stone.Black, BlackTakes);

        afterTake.TryPlay(Stone.White, WhiteRetakes).Reason.Should().Be(IllegalMoveReason.Ko);
    }

    [Fact]
    public void Recapture_is_legal_after_an_exchange_elsewhere()
    {
        BoardState board = KoPosition(RuleSet.Japanese)
            .Play(Stone.Black, BlackTakes)
            .Play(Stone.White, P(4, 4)) // ko threat
            .Play(Stone.Black, P(4, 3)); // answer

        MoveResult retake = board.TryPlay(Stone.White, WhiteRetakes);

        retake.IsLegal.Should().BeTrue();
        retake.Captured.Should().Equal(BlackTakes);
        retake.State!.KoPoint.Should().Be(BlackTakes);
    }

    [Fact]
    public void Ko_point_is_cleared_by_a_pass()
    {
        BoardState board = KoPosition(RuleSet.Japanese).Play(Stone.Black, BlackTakes).Pass(Stone.White);

        board.KoPoint.Should().BeNull();
    }

    [Fact]
    public void Ko_point_is_cleared_by_any_other_move()
    {
        BoardState board = KoPosition(RuleSet.Japanese).Play(Stone.Black, BlackTakes).Play(Stone.White, P(4, 4));

        board.KoPoint.Should().BeNull();
    }

    [Fact]
    public void Ko_restriction_only_applies_to_the_opponent_of_the_capturer()
    {
        BoardState afterTake = KoPosition(RuleSet.Japanese).Play(Stone.Black, BlackTakes);

        // Black filling the ko (out of turn, as SGF allows) is not a ko violation.
        afterTake.TryPlay(Stone.Black, WhiteRetakes).IsLegal.Should().BeTrue();
    }

    [Fact]
    public void Capturing_two_stones_does_not_create_a_ko()
    {
        MoveResult result = Parse(
            ". X X . .",
            "X O O . .",
            ". X X . .").TryPlay(Stone.Black, P(3, 1));

        result.Captured.Should().HaveCount(2);
        result.State!.KoPoint.Should().BeNull();
    }

    [Fact]
    public void Immediate_recapture_is_reported_as_ko_under_superko_rules_too()
    {
        BoardState afterTake = KoPosition(RuleSet.Chinese).Play(Stone.Black, BlackTakes);

        afterTake.TryPlay(Stone.White, WhiteRetakes).Reason.Should().Be(IllegalMoveReason.Ko);
    }
}
