using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

/// <summary>
/// Triple ko: three independent kos. Starting position (Black to move):
/// ko 1 held by Black, ko 2 and ko 3 held by White. Six alternating captures return to the start.
/// </summary>
public sealed class SuperkoTests
{
    private static BoardState TripleKo(RuleSet rules) => Parse(rules,
        ". X O . . . .", // ko 1 (rows 0-2): black holds
        "X . X O . . .",
        ". X O . . . .",
        ". . . . . . .",
        ". X O . . . .", // ko 2 (rows 4-6): white holds
        "X O . O . . .",
        ". X O . . . .",
        ". . . . . . .",
        ". X O . . . .", // ko 3 (rows 8-10): white holds
        "X O . O . . .",
        ". X O . . . .");

    private static readonly (Stone Color, Point Point)[] Cycle =
    [
        (Stone.Black, new Point(2, 5)), // takes ko 2
        (Stone.White, new Point(1, 1)), // takes ko 1
        (Stone.Black, new Point(2, 9)), // takes ko 3
        (Stone.White, new Point(1, 5)), // takes ko 2
        (Stone.Black, new Point(2, 1)), // takes ko 1
        (Stone.White, new Point(1, 9)), // takes ko 3 → recreates the starting position
    ];

    private static BoardState PlayFirstFive(BoardState start)
    {
        BoardState board = start;
        foreach ((Stone color, Point point) in Cycle.Take(5))
        {
            MoveResult r = board.TryPlay(color, point);
            r.IsLegal.Should().BeTrue($"{color} at {point} is an ordinary ko capture");
            r.Captured.Should().HaveCount(1);
            board = r.State!;
        }

        return board;
    }

    [Fact]
    public void Positional_superko_forbids_recreating_the_start_of_a_triple_ko()
    {
        BoardState start = TripleKo(RuleSet.Chinese);
        BoardState board = PlayFirstFive(start);

        MoveResult sixth = board.TryPlay(Cycle[5].Color, Cycle[5].Point);

        sixth.IsLegal.Should().BeFalse();
        sixth.Reason.Should().Be(IllegalMoveReason.Superko);
    }

    [Fact]
    public void Situational_superko_also_forbids_it_when_the_same_player_is_to_move()
    {
        BoardState board = PlayFirstFive(TripleKo(RuleSet.Aga));

        board.TryPlay(Cycle[5].Color, Cycle[5].Point).Reason.Should().Be(IllegalMoveReason.Superko);
    }

    [Fact]
    public void Japanese_rules_allow_the_repetition()
    {
        BoardState start = TripleKo(RuleSet.Japanese);
        BoardState board = PlayFirstFive(start);

        MoveResult sixth = board.TryPlay(Cycle[5].Color, Cycle[5].Point);

        sixth.IsLegal.Should().BeTrue();
        sixth.State!.Hash.Should().Be(start.Hash, "the cycle returns to the exact starting position");
        Render(sixth.State).Should().Be(Render(start));
    }
}
