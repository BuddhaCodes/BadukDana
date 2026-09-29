using Hoshi.App.ViewModels;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class GameViewModelTests
{
    private static Point P(int x, int y) => new(x, y);

    [Fact]
    public void Starts_with_an_empty_19x19_board_and_black_to_move()
    {
        var game = new GameViewModel();

        game.Board.Width.Should().Be(19);
        game.Board.ToMove.Should().Be(Stone.Black);
        game.MoveNumber.Should().Be(0);
        game.StatusText.Should().Be("Juegan negras");
        game.UndoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Playing_alternates_colours_and_records_the_last_move()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);

        game.PlayCommand.Execute(P(2, 2));
        game.PlayCommand.Execute(P(6, 6));

        game.Board[P(2, 2)].Should().Be(Stone.Black);
        game.Board[P(6, 6)].Should().Be(Stone.White);
        game.LastMove.Should().Be(P(6, 6));
        game.MoveNumber.Should().Be(2);
        game.MoveNumberText.Should().Be("Jugada 2");
    }

    [Fact]
    public void Illegal_moves_leave_the_position_unchanged_and_explain_why()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P(4, 4));
        BoardState before = game.Board;

        game.PlayCommand.Execute(P(4, 4));

        game.Board.Should().BeSameAs(before);
        game.StatusText.Should().Be("Jugada ilegal en E5: punto ocupado");
    }

    [Fact]
    public void Undo_restores_the_previous_position()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P(2, 2));
        BoardState afterFirst = game.Board;
        game.PlayCommand.Execute(P(3, 3));

        game.UndoCommand.Execute(null);

        game.Board.Should().BeSameAs(afterFirst);
        game.LastMove.Should().Be(P(2, 2));
        game.MoveNumber.Should().Be(1);
    }

    [Fact]
    public void Captures_are_shown()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        foreach (Point p in new[] { P(1, 0), P(0, 0), P(8, 8) })
        {
            game.PlayCommand.Execute(p);
        }

        game.PlayCommand.Execute(P(7, 7)); // white elsewhere
        game.PlayCommand.Execute(P(0, 1)); // black captures the corner stone

        game.Board.BlackCaptures.Should().Be(1);
        game.CapturesText.Should().Be("Capturas ● 1 · ○ 0");
    }

    [Fact]
    public void Two_consecutive_passes_end_the_game_and_undo_resumes_it()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);

        game.PassCommand.Execute(null);
        game.StatusText.Should().Be("Negras pasa · juegan blancas");
        game.PassCommand.Execute(null);

        game.IsGameOver.Should().BeTrue();
        game.PlayCommand.CanExecute(P(0, 0)).Should().BeFalse();
        game.StatusText.Should().Be("Partida terminada: dos pases seguidos");

        game.UndoCommand.Execute(null);
        game.IsGameOver.Should().BeFalse();
        game.PlayCommand.CanExecute(P(0, 0)).Should().BeTrue();
    }

    [Fact]
    public void Ghost_stone_only_appears_on_legal_points()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P(4, 4));

        game.HoverPoint = P(3, 3);
        game.GhostStone.Should().Be(Stone.White);

        game.HoverPoint = P(4, 4);
        game.GhostStone.Should().Be(Stone.Empty, "the point is occupied");

        game.HoverPoint = null;
        game.GhostStone.Should().Be(Stone.Empty);
    }

    [Fact]
    public void New_game_resets_with_the_requested_size()
    {
        var game = new GameViewModel(19, RuleSet.Japanese);
        game.PlayCommand.Execute(P(3, 3));

        game.NewGameCommand.Execute(13);

        game.Board.Width.Should().Be(13);
        game.MoveNumber.Should().Be(0);
        game.LastMove.Should().BeNull();
        game.UndoCommand.CanExecute(null).Should().BeFalse();
    }
}
