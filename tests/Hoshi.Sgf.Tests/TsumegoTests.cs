using Hoshi.Core;
using Hoshi.Sgf.Problems;

namespace Hoshi.Sgf.Tests;

public sealed class TsumegoTests
{
    // Black to play: e6 (4,3) captures the white stone; f4 is a wrong try with a refutation.
    private const string Atari = "(;GM[1]FF[4]SZ[9]GN[Atari]PL[B]AB[de][fe][ed]AW[ee]C[Capture]" +
        "(;B[ef]C[RIGHT])" +
        "(;B[ff]C[WRONG: too slow];W[gf]))";

    // Black plays the double atari, White saves one stone, Black captures the other.
    private const string DoubleAtari = "(;GM[1]FF[4]SZ[9]PL[B]AB[ce][dd][ge][fd]AW[de][fe]" +
        ";B[ee];W[df];B[ff]C[RIGHT])";

    private static TsumegoProblem Load(string sgf) => TsumegoProblem.FromTree(SgfParser.ParseCollection(sgf).Games[0]);

    [Fact]
    public void The_problem_starts_from_the_setup_with_the_player_to_play()
    {
        TsumegoProblem p = Load(Atari);
        p.Name.Should().Be("Atari");
        p.ToPlay.Should().Be(Stone.Black);
        p.Instructions.Should().Be("Capture");
        p.Start[new Point(4, 4)].Should().Be(Stone.White);
        p.Start.ToMove.Should().Be(Stone.Black);
    }

    [Fact]
    public void The_right_move_solves_it()
    {
        var attempt = new TsumegoAttempt(Load(Atari));
        attempt.Play(new Point(4, 5)).Should().Be(TsumegoOutcome.Solved);
        attempt.Board[new Point(4, 4)].Should().Be(Stone.Empty, "the white stone was captured");
        attempt.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void A_move_off_the_tree_or_marked_wrong_fails_and_shows_the_refutation()
    {
        var attempt = new TsumegoAttempt(Load(Atari));
        attempt.Play(new Point(0, 0)).Should().Be(TsumegoOutcome.Wrong);
        attempt.Board[new Point(0, 0)].Should().Be(Stone.Black, "the stone stays to show what was tried");

        attempt = new TsumegoAttempt(Load(Atari));
        attempt.Play(new Point(5, 5)).Should().Be(TsumegoOutcome.Wrong);
        attempt.Board[new Point(6, 5)].Should().Be(Stone.White, "the refutation is played");
        attempt.Comment.Should().Be("WRONG: too slow");
    }

    [Fact]
    public void The_opponent_answers_from_the_tree_until_the_line_ends()
    {
        var attempt = new TsumegoAttempt(Load(DoubleAtari));
        attempt.Play(new Point(4, 4)).Should().Be(TsumegoOutcome.Continue);
        attempt.Board[new Point(3, 5)].Should().Be(Stone.White, "White saves one stone");
        attempt.LastMove.Should().Be(new Point(3, 5));
        attempt.Play(new Point(5, 5)).Should().Be(TsumegoOutcome.Solved);
        attempt.Board[new Point(5, 4)].Should().Be(Stone.Empty);
    }

    [Fact]
    public void The_answer_is_the_next_move_of_a_right_line()
    {
        var attempt = new TsumegoAttempt(Load(Atari));
        attempt.Hint.Should().Be(new Point(4, 5), "the WRONG branch is never shown as the answer");
        attempt.Play(new Point(4, 5));
        attempt.Hint.Should().BeNull();
    }

    [Fact]
    public void Illegal_moves_are_rejected_without_failing()
    {
        var attempt = new TsumegoAttempt(Load(Atari));
        attempt.Play(new Point(4, 4)).Should().Be(TsumegoOutcome.Illegal);
        attempt.IsFinished.Should().BeFalse();
    }
}
