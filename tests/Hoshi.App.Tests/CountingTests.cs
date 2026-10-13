using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.Controls;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Counting at the end of a local game: dead stones, the score, accept or keep playing.</summary>
[Collection("Language")]
public sealed class CountingTests
{
    // Black walls off the left, White the right; a lone white stone inside Black's area.
    private static GameViewModel FinishedGame()
    {
        var game = new GameViewModel();
        game.Load(GameTree.Create(9, RuleSet.Japanese, komi: 6.5), path: null);
        var moves = new List<Point>();
        for (int y = 0; y < 9; y++)
        {
            moves.Add(new Point(3, y));
            moves.Add(new Point(5, y));
        }

        foreach (Point p in moves)
        {
            game.PlayCommand.Execute(p);
        }

        game.PlayCommand.Execute(new Point(7, 4)); // black, inside White's area: dead
        game.PlayCommand.Execute(new Point(1, 1)); // white, inside Black's area: dead
        return game;
    }

    [AvaloniaFact]
    public void Two_passes_start_counting_and_a_tap_marks_a_group_dead()
    {
        GameViewModel game = FinishedGame();
        var counting = new CountingViewModel(game);
        game.PassCommand.Execute(null);
        counting.IsActive.Should().BeFalse();
        game.PassCommand.Execute(null);
        counting.IsActive.Should().BeTrue("the second pass ends the game");

        // Nothing marked: the lone stones count as alive and spoil both areas.
        counting.Result!.BlackTerritory.Should().Be(0);

        game.PlayCommand.Execute(new Point(1, 1));
        game.PlayCommand.Execute(new Point(7, 4));
        counting.Dead.Should().BeEquivalentTo([new Point(1, 1), new Point(7, 4)]);
        game.Board[new Point(1, 1)].Should().Be(Stone.White, "a tap while counting never plays");
        game.Markers.Should().Contain(m => m.Point == new Point(1, 1) && m.Kind == MarkupKind.Cross);
        counting.Result!.BlackTerritory.Should().Be(27);
        counting.Result.WhiteTerritory.Should().Be(27);
        counting.Result.Winner.Should().Be(Stone.White);
        counting.Result.Margin.Should().Be(6.5);
        counting.ResultText.Should().NotBeEmpty();
        counting.Territory.Should().NotBeNull();

        counting.AcceptCommand.Execute(null);
        counting.IsActive.Should().BeFalse();
        game.Tree.Info.Result.Should().Be("W+6.5");
        game.Markers.Should().NotContain(m => m.Kind == MarkupKind.Cross);
    }

    [AvaloniaFact]
    public void Keep_playing_takes_back_the_last_pass()
    {
        GameViewModel game = FinishedGame();
        var counting = new CountingViewModel(game);
        game.PassCommand.Execute(null);
        game.PassCommand.Execute(null);
        int moves = game.MoveNumber;

        counting.KeepPlayingCommand.Execute(null);
        counting.IsActive.Should().BeFalse();
        game.IsGameOver.Should().BeFalse();
        game.MoveNumber.Should().Be(moves - 1);
        game.PlayCommand.Execute(new Point(4, 4));
        game.Board[new Point(4, 4)].Should().NotBe(Stone.Empty, "the board plays again");
    }

    [AvaloniaFact]
    public void The_main_window_shows_the_count()
    {
        GameViewModel game = FinishedGame();
        var vm = new MainWindowViewModel(game);
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        game.PassCommand.Execute(null);
        game.PassCommand.Execute(null);
        window.FindDeep<Border>("CountingCard")!.IsVisible.Should().BeTrue();
        window.FindDeep<GoBoardControl>("Board")!.Territory.Should().BeSameAs(vm.Counting.Territory);
        window.Close();
    }
}
