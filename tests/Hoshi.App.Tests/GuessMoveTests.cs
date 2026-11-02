using FluentAssertions;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>"Guess the move" on the game on the board.</summary>
public sealed class GuessMoveTests
{
    private static GameViewModel Game(string sgf)
    {
        var game = new GameViewModel();
        game.Load(SgfParser.Parse(sgf), path: null);
        game.GoToNodeCommand.Execute(game.Tree.Root);
        return game;
    }

    [Fact]
    public void Right_guesses_move_on_two_misses_show_the_move_and_the_score_counts_first_tries()
    {
        GameViewModel game = Game("(;GM[1]SZ[9];B[ee];W[cc];B[gg];W[cg])");
        var study = new StudyViewModel(game);
        GuessMoveViewModel guess = study.Guess;

        guess.StartCommand.Execute(null);
        guess.IsActive.Should().BeTrue();
        study.BoardClickCommand.Execute(new Point(4, 4)); // E5 = ee: right at once
        game.Board[new Point(4, 4)].Should().Be(Stone.Black);
        guess.Message.Should().Be(Tr.T("Guess.Right"));

        study.BoardClickCommand.Execute(new Point(0, 0)); // wrong
        guess.Message.Should().Be(Tr.T("Guess.TryAgain"));
        study.BoardDrawings.Should().ContainSingle(d => d.Id == "guess-wrong");
        study.BoardClickCommand.Execute(new Point(2, 2)); // cc: right at the second try
        guess.Message.Should().Be(Tr.T("Guess.RightSecond"));

        study.BoardClickCommand.Execute(new Point(0, 0));
        study.BoardClickCommand.Execute(new Point(1, 0)); // two misses: the move is shown and played
        game.Board[new Point(6, 6)].Should().Be(Stone.Black);
        study.BoardDrawings.Should().Contain(d => d.Id == "guess-answer" && d.Points[0] == new Point(6, 6));

        guess.RevealCommand.Execute(null); // gives up the last one: the game is over
        guess.IsActive.Should().BeFalse();
        (guess.Guessed, guess.FirstTry, guess.Total).Should().Be((2, 1, 4));
    }

    [Fact]
    public void Guessing_one_colour_plays_the_other_by_itself()
    {
        GameViewModel game = Game("(;GM[1]SZ[9];B[ee];W[cc];B[gg];W[cg])");
        var study = new StudyViewModel(game);
        study.Guess.Side = study.Guess.Sides.Single(s => s.Side == GuessSide.White);

        study.Guess.StartCommand.Execute(null);
        game.Board[new Point(4, 4)].Should().Be(Stone.Black, "Black's move was played without asking");
        study.BoardClickCommand.Execute(new Point(2, 2));
        game.Board[new Point(6, 6)].Should().Be(Stone.Black);
        study.Guess.Total.Should().Be(1);
    }

    [Fact]
    public void Nothing_to_guess_at_the_end_of_a_game()
    {
        GameViewModel game = Game("(;GM[1]SZ[9];B[ee])");
        game.GoToNodeCommand.Execute(game.Tree.Root.Children[0]);
        var study = new StudyViewModel(game);
        study.Guess.CanStart.Should().BeFalse();
        study.Guess.StartCommand.Execute(null);
        study.Guess.IsActive.Should().BeFalse();
    }
}
