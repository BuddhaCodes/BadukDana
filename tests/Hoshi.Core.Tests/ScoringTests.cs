using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class ScoringTests
{
    // Black owns columns 0-1 (with one dead white stone inside), White owns column 4.
    private static readonly string[] DeadStoneInside =
    [
        ". . X O .",
        ". . X O .",
        ". O X O .",
        ". . X O .",
        ". . X O .",
    ];

    private static readonly Point DeadWhite = new(1, 2);

    // Seki: neither group has an eye; the two shared liberties at (1,0) and (1,1) are neutral.
    private static readonly string[] Seki =
    [
        "X . O O",
        "X . O O",
        "X X O O",
        "X X O O",
    ];

    [Fact]
    public void Territory_scoring_counts_territory_plus_prisoners_including_dead_stones()
    {
        BoardState board = Parse(RuleSet.Japanese, DeadStoneInside);

        ScoreResult score = Scoring.Score(board, [DeadWhite], komi: 6.5);

        score.Method.Should().Be(ScoringMethod.Territory);
        score.BlackTerritory.Should().Be(10);
        score.WhiteTerritory.Should().Be(5);
        score.BlackPrisoners.Should().Be(1);
        score.WhitePrisoners.Should().Be(0);
        score.BlackScore.Should().Be(11);
        score.WhiteScore.Should().Be(11.5);
        score.Winner.Should().Be(Stone.White);
        score.Margin.Should().Be(0.5);
    }

    [Fact]
    public void Area_scoring_counts_stones_plus_territory_and_ignores_prisoners()
    {
        BoardState board = Parse(RuleSet.Chinese, DeadStoneInside);

        ScoreResult score = Scoring.Score(board, [DeadWhite], komi: 7.5);

        score.Method.Should().Be(ScoringMethod.Area);
        score.BlackStones.Should().Be(5);
        score.WhiteStones.Should().Be(5, "the dead stone is removed before counting");
        score.BlackScore.Should().Be(15);
        score.WhiteScore.Should().Be(17.5);
        score.Margin.Should().Be(2.5);
    }

    [Fact]
    public void Unmarked_dead_stone_makes_the_region_neutral()
    {
        BoardState board = Parse(RuleSet.Japanese, DeadStoneInside);

        ScoreResult score = Scoring.Score(board, [], komi: 6.5);

        score.BlackTerritory.Should().Be(0);
        score.Dame.Should().HaveCount(9);
        score.WhiteTerritory.Should().Be(5);
    }

    [Fact]
    public void Ownership_map_marks_territory_dead_stones_and_dame()
    {
        ScoreResult score = Scoring.Score(Parse(RuleSet.Japanese, DeadStoneInside), [DeadWhite], komi: 6.5);

        score.OwnerAt(DeadWhite).Should().Be(Stone.Black);
        score.OwnerAt(P(0, 0)).Should().Be(Stone.Black);
        score.OwnerAt(P(4, 4)).Should().Be(Stone.White);
        score.OwnerAt(P(2, 0)).Should().Be(Stone.Black, "a living stone belongs to its colour");
    }

    [Fact]
    public void Seki_liberties_are_neutral_under_area_scoring()
    {
        ScoreResult score = Scoring.Score(Parse(RuleSet.Chinese, Seki), [], komi: 7.5);

        score.Dame.Should().BeEquivalentTo([P(1, 0), P(1, 1)]);
        score.BlackTerritory.Should().Be(0);
        score.WhiteTerritory.Should().Be(0);
        score.BlackScore.Should().Be(6);
        score.WhiteScore.Should().Be(15.5);
    }

    [Fact]
    public void Seki_liberties_are_neutral_under_territory_scoring()
    {
        ScoreResult score = Scoring.Score(Parse(RuleSet.Japanese, Seki), [], komi: 0);

        score.BlackScore.Should().Be(0);
        score.WhiteScore.Should().Be(0);
        score.Winner.Should().Be(Stone.Empty, "an equal score is jigo");
        score.Margin.Should().Be(0);
    }

    [Fact]
    public void Captures_made_during_play_count_as_prisoners()
    {
        BoardState board = Parse(RuleSet.Japanese,
            ". X . O .",
            "X O X O .",
            ". . X O .",
            ". . X O .");

        BoardState after = board.Play(Stone.Black, P(1, 2));
        ScoreResult score = Scoring.Score(after, [], komi: 0);

        after.BlackCaptures.Should().Be(1);
        score.BlackPrisoners.Should().Be(1);
        score.BlackTerritory.Should().Be(5, "(0,0), (1,1) and the three points at the bottom left");
        score.WhiteTerritory.Should().Be(4);
        score.Dame.Should().Equal(P(2, 0));
        score.BlackScore.Should().Be(6);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    public void Chinese_rules_compensate_white_one_point_per_handicap_stone(int handicap, int expected)
    {
        ScoreResult score = Scoring.Score(Parse(RuleSet.Chinese, Seki), [], komi: 0.5, handicap);

        score.HandicapCompensation.Should().Be(expected);
        score.WhiteScore.Should().Be(8 + 0.5 + expected);
    }

    [Fact]
    public void Aga_rules_compensate_white_handicap_minus_one()
    {
        Scoring.Score(Parse(RuleSet.Aga, Seki), [], komi: 0.5, handicap: 3).HandicapCompensation.Should().Be(2);
    }

    [Fact]
    public void Japanese_rules_do_not_compensate_handicap()
    {
        Scoring.Score(Parse(RuleSet.Japanese, Seki), [], komi: 0.5, handicap: 3).HandicapCompensation.Should().Be(0);
    }

    [Fact]
    public void Dead_stone_list_may_only_contain_stones()
    {
        FluentActions.Invoking(() => Scoring.Score(Parse(RuleSet.Japanese, Seki), [P(1, 0)], komi: 0))
            .Should().Throw<ArgumentException>();
    }
}
