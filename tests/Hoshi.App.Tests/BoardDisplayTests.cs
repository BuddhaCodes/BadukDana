using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Coordinates and move numbers on the board (Preferences → Appearance), on the desktop and on phones.</summary>
[Collection("Language")]
public sealed class BoardDisplayTests
{
    [AvaloniaFact]
    public void Move_numbers_label_the_stones_still_on_the_board()
    {
        var game = new GameViewModel();
        game.Load(Hoshi.Sgf.GameTree.Create(9, RuleSet.Japanese), path: null);
        // White (1,0) is captured by black's third move.
        foreach ((int x, int y) in new[] { (0, 0), (1, 0), (2, 0), (8, 8), (1, 1) })
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        game.MoveNumberLabels.Should().BeEmpty("numbers are off by default");
        game.MoveNumberMode = MoveNumbers.All;
        game.MoveNumberLabels.Should().BeEquivalentTo(new Dictionary<Point, int>
        {
            [new Point(0, 0)] = 1,
            [new Point(2, 0)] = 3,
            [new Point(8, 8)] = 4,
            [new Point(1, 1)] = 5,
        });

        for (int i = 0; i < 12; i++)
        {
            game.PlayCommand.Execute(new Point(i % 9, 4 + (2 * (i / 9))));
        }

        game.MoveNumberMode = MoveNumbers.Recent;
        game.MoveNumberLabels.Should().HaveCount(GameViewModel.RecentMoveNumbers);
        game.MoveNumberLabels.Values.Min().Should().Be(game.MoveNumber - GameViewModel.RecentMoveNumbers + 1);
    }

    [AvaloniaFact]
    public void Coordinates_and_numbers_follow_the_preferences_at_once()
    {
        bool was = PlatformInfo.IsPhone;
        try
        {
            PlatformInfo.ShowCoordinates(new AppSettings()).Should().Be(!PlatformInfo.IsPhone);
            PlatformInfo.IsPhone = true;
            PlatformInfo.ShowCoordinates(new AppSettings()).Should().BeFalse("phones give the board the room");
            PlatformInfo.ShowCoordinates(new AppSettings { ShowCoordinates = true }).Should().BeTrue();
        }
        finally
        {
            PlatformInfo.IsPhone = was;
        }

        var settings = new TestSettings();
        var preferences = new PreferencesViewModel(new ThemeService(settings), settings);
        var game = new GameViewModel();
        var main = new MainWindowViewModel(game, settings: settings, preferences: preferences);
        var window = new MainWindow(main) { Width = 1200, Height = 820 };
        window.Show();
        GoBoardControl board = window.FindDeep<GoBoardControl>("Board")!;
        board.ShowCoordinates.Should().BeTrue();

        preferences.ShowCoordinates = false;
        board.ShowCoordinates.Should().BeFalse();
        settings.Current.ShowCoordinates.Should().BeFalse();

        game.PlayCommand.Execute(new Point(3, 3));
        preferences.SelectedMoveNumbers = preferences.MoveNumbersOptions[2];
        settings.Current.MoveNumbers.Should().Be(MoveNumbers.All);
        board.MoveNumbers.Should().ContainKey(new Point(3, 3)).WhoseValue.Should().Be(1);
        window.Close();
    }

    [AvaloniaFact]
    public void The_sidebar_keeps_the_width_it_was_dragged_to()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { SidebarWidth = 360 });
        var main = new MainWindowViewModel(new GameViewModel(), settings: settings);
        var window = new MainWindow(main) { Width = 1200, Height = 820 };
        window.Show();
        window.UpdateLayout();
        window.FindDeep<Grid>("Sidebar")!.Bounds.Width.Should().BeApproximately(360, 1);

        main.RememberSidebarWidth(300.4);
        settings.Current.SidebarWidth.Should().Be(300);
        window.Close();
    }
}

/// <summary>Accessibility: interface size, high contrast, the move box and the board read aloud.</summary>
[Collection("Language")]
public sealed class AccessibilityTests
{
    [AvaloniaFact]
    public void The_board_describes_itself_and_typed_moves_are_played()
    {
        Hoshi.Core.Localization.Tr.SetLanguage(Hoshi.Core.Localization.Tr.English);
        try
        {
            var game = new GameViewModel { ConfirmMoves = true };
            game.Load(Hoshi.Sgf.GameTree.Create(9, RuleSet.Japanese), path: null);
            game.BoardDescription.Should().Be("Start of the game. Black to play. Captures: black 0, white 0.");

            game.PlayTyped("d4").Should().BeTrue("typing is the confirmation");
            game.Board[new Point(3, 5)].Should().Be(Stone.Black);
            game.BoardDescription.Should().StartWith("Move 1: Black D4. White to play.");
            game.PlayTyped("Z9").Should().BeFalse();
            game.StatusText.Should().Contain("Z9");
            game.PlayTyped("pass").Should().BeTrue();
            game.MoveNumber.Should().Be(2);
        }
        finally
        {
            Hoshi.Core.Localization.Tr.SetLanguage(Hoshi.Core.Localization.Tr.English);
        }
    }

    [AvaloniaFact]
    public void Interface_size_and_the_move_box_follow_preferences()
    {
        var settings = new TestSettings();
        var preferences = new PreferencesViewModel(new ThemeService(settings), settings);
        var game = new GameViewModel();
        var main = new MainWindowViewModel(game, settings: settings, preferences: preferences);
        var window = new MainWindow(main) { Width = 1400, Height = 900 };
        window.Show();
        TextBox box = window.FindDeep<TextBox>("MoveEntryBox")!;
        box.IsVisible.Should().BeFalse();

        preferences.SelectedUiScale = preferences.UiScaleOptions[2];
        preferences.ShowMoveEntry = true;
        settings.Current.UiScale.Should().Be(1.3);
        settings.Current.ShowMoveEntry.Should().BeTrue();
        main.UiScale.Should().Be(1.3);
        window.UpdateLayout();
        box.IsVisible.Should().BeTrue();
        window.FindDeep<LayoutTransformControl>("UiScaleRoot")!.LayoutTransform!.Value.M11.Should().BeApproximately(1.3, 1e-9);

        main.MoveEntryText = "Q16";
        main.PlayEntryCommand.Execute(null);
        game.Board[new Point(15, 3)].Should().Be(Stone.Black);
        main.MoveEntryText.Should().BeEmpty();
        window.FindDeep<GoBoardControl>("Board")!.GetValue(Avalonia.Automation.AutomationProperties.NameProperty).Should().Contain("Q16");
        window.Close();
    }

    [Fact]
    public void The_high_contrast_theme_is_flat_and_still()
    {
        HoshiThemes.All.Should().Contain(HoshiThemes.HighContrast);
        HoshiThemes.HighContrast.Board.StoneShadow.A.Should().Be(0);
        HoshiThemes.HighContrast.Board.Texture.Should().BeNull();
        HoshiThemes.HighContrast.Text.Should().Be(Avalonia.Media.Colors.White);
    }
}
