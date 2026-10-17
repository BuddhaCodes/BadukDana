using Hoshi.Core;
using Hoshi.Sgf.Problems;

namespace Hoshi.Sgf.Tests;

public sealed class TsumegoWriterTests
{
    [Fact]
    public void A_best_move_problem_reads_back_with_the_right_and_the_wrong_answer()
    {
        BoardState before = BoardState.Create(9).Play(Stone.Black, new Point(2, 2)).Play(Stone.White, new Point(6, 6));
        GameTree tree = TsumegoWriter.BestMove(before, Stone.Black, new Point(6, 2), new Point(4, 4), "Ana–Luis, move 3", "Find the best move", "You played E5 (−6.5).");

        string sgf = SgfWriter.Write(tree);
        TsumegoProblem problem = TsumegoProblem.FromTree(SgfParser.ParseCollection(sgf).Games[0]);
        problem.ToPlay.Should().Be(Stone.Black);
        problem.Start[2, 2].Should().Be(Stone.Black);
        problem.Start[6, 6].Should().Be(Stone.White);
        problem.Name.Should().Be("Ana–Luis, move 3");
        problem.Instructions.Should().Be("Find the best move");

        var wrong = new TsumegoAttempt(problem);
        wrong.Play(new Point(4, 4)).Should().Be(TsumegoOutcome.Wrong);
        wrong.Comment.Should().Contain("You played E5");
        new TsumegoAttempt(problem).Play(new Point(0, 8)).Should().Be(TsumegoOutcome.Wrong, "off the tree");
        new TsumegoAttempt(problem).Play(new Point(6, 2)).Should().Be(TsumegoOutcome.Solved);
    }

    [Fact]
    public void Without_a_played_move_there_is_only_the_answer()
    {
        GameTree tree = TsumegoWriter.BestMove(BoardState.Create(9).Play(Stone.Black, new Point(4, 4)), Stone.White, new Point(2, 2), null, "x");
        tree.Root.Children.Should().ContainSingle().Which.Comment.Should().Be("RIGHT");
        FluentActions.Invoking(() => TsumegoWriter.BestMove(BoardState.Create(9), Stone.Empty, new Point(2, 2), null, "x")).Should().Throw<ArgumentException>();
    }
}
