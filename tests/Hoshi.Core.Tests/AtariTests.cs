using static Hoshi.Core.Tests.Boards;

namespace Hoshi.Core.Tests;

public sealed class AtariTests
{
    [Fact]
    public void Groups_with_a_single_liberty_are_in_atari()
    {
        BoardState board = Parse(
            ". X O O .",
            ". X O X .",
            ". . X . .",
            "O . . . .",
            "X . . . .");

        IReadOnlyList<AtariGroup> groups = Atari.Groups(board);

        groups.Should().HaveCount(2);
        groups.Should().ContainSingle(g => g.Color == Stone.White && g.Stones.Count == 3)
            .Which.Liberty.Should().Be(P(4, 0));
        groups.Should().ContainSingle(g => g.Color == Stone.Black && g.Stones.Count == 1 && g.Stones[0] == P(0, 4));
    }

    [Fact]
    public void Only_groups_that_were_not_already_in_atari_are_new()
    {
        BoardState before = Parse(
            ". X . .",
            "X O . .",
            ". . . .",
            ". . . .");
        BoardState after = before.TryPlay(Stone.Black, P(2, 1)).State!;

        Atari.NewlyInAtari(before, after).Should().ContainSingle().Which.Stones.Should().Equal(P(1, 1));

        // White extends to two liberties; black then puts it back in atari: new again.
        BoardState extended = after.TryPlay(Stone.White, P(1, 2)).State!;
        Atari.Groups(extended).Should().BeEmpty();
        BoardState again = extended.TryPlay(Stone.Black, P(2, 2)).State!;
        BoardState atariAgain = again.TryPlay(Stone.White, P(3, 3)).State!.TryPlay(Stone.Black, P(0, 2)).State!;
        Atari.NewlyInAtari(again, atariAgain).Should().ContainSingle().Which.Stones.Should().BeEquivalentTo([P(1, 1), P(1, 2)]);

        // A move elsewhere does not re-announce a group that stays in atari.
        BoardState elsewhere = atariAgain.TryPlay(Stone.White, P(3, 0)).State!;
        Atari.NewlyInAtari(atariAgain, elsewhere).Should().BeEmpty();
    }
}
