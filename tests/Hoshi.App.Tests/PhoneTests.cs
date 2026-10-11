using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Engines;
using Hoshi.App.Services.Phone;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>The phone layout (Android) and tap-and-confirm, exercised on the desktop.</summary>
[Collection("Language")]
public sealed class PhoneTests
{
    private static (Window Window, PhoneShellViewModel Shell) Phone(GameViewModel? game = null, PhoneNavigation? navigation = null, IAnalysisEngine? engine = null, int width = 400, int height = 860)
    {
        game ??= new GameViewModel();
        navigation ??= new PhoneNavigation();
        var main = new MainWindowViewModel(game, engine: engine, ui: new ImmediateDispatcher(), dialogs: navigation, preferencesWindow: navigation);
        var shell = new PhoneShellViewModel(main, navigation);
        var window = new Window { Width = width, Height = height, Content = new PhoneView { DataContext = shell } };
        window.Show();
        return (window, shell);
    }

    /// <summary>A tap on a button: its Click handlers and its command.</summary>
    private static void Press(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    [Fact]
    public void Tap_and_confirm_plays_on_the_second_tap_of_the_same_point()
    {
        var game = new GameViewModel { ConfirmMoves = true };
        game.PlayCommand.Execute(new Point(3, 3));
        game.MoveNumber.Should().Be(0);
        game.PendingMove.Should().Be(new Point(3, 3));
        game.PendingStone.Should().Be(Stone.Black);

        game.PlayCommand.Execute(new Point(4, 4));
        game.PendingMove.Should().Be(new Point(4, 4), "a tap elsewhere moves the waiting stone");
        game.PlayCommand.Execute(new Point(4, 4));
        game.MoveNumber.Should().Be(1);
        game.Board[new Point(4, 4)].Should().Be(Stone.Black);
        game.PendingMove.Should().BeNull();

        game.PlayCommand.Execute(new Point(10, 10));
        game.GoBackCommand.Execute(null);
        game.PendingMove.Should().BeNull("moving through the game drops the waiting stone");

        game.GoForwardCommand.Execute(null);
        game.PlayCommand.Execute(new Point(4, 4));
        game.PendingMove.Should().BeNull("an occupied point is not a stone to confirm");
        game.ConfirmMoves = false;
        game.PlayCommand.Execute(new Point(15, 15));
        game.MoveNumber.Should().Be(2, "without confirmation one tap plays");
    }

    [Fact]
    public void Tap_and_confirm_follows_the_preference_and_defaults_on_for_phones()
    {
        bool was = PlatformInfo.IsPhone;
        try
        {
            PlatformInfo.IsPhone = true;
            PlatformInfo.ConfirmMoves(new AppSettings()).Should().BeTrue();
            PlatformInfo.IsPhone = false;
            PlatformInfo.ConfirmMoves(new AppSettings()).Should().BeFalse();
            PlatformInfo.ConfirmMoves(new AppSettings { ConfirmMoves = true }).Should().BeTrue();
        }
        finally
        {
            PlatformInfo.IsPhone = was;
        }

        var settings = new TestSettings();
        var themes = new ThemeService(settings);
        var preferences = new PreferencesViewModel(themes, settings);
        var game = new GameViewModel();
        _ = new MainWindowViewModel(game, settings: settings, preferences: preferences);
        game.ConfirmMoves.Should().BeFalse();
        preferences.ConfirmMoves = true;
        game.ConfirmMoves.Should().BeTrue("the board follows at once");
        settings.Current.ConfirmMoves.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Sheets_open_one_at_a_time_and_follow_the_study_and_the_analysis()
    {
        (Window window, PhoneShellViewModel shell) = Phone();
        window.FindDeep<Border>("PhoneSheet")!.IsVisible.Should().BeFalse();

        shell.ToggleSheetCommand.Execute(PhoneSheet.Moves);
        window.FindDeep<Border>("PhoneSheet")!.IsVisible.Should().BeTrue();
        window.FindDeep<Grid>("PhoneMovesSheet")!.IsVisible.Should().BeTrue();

        shell.ToggleSheetCommand.Execute(PhoneSheet.Study);
        shell.Main.Study.IsActive.Should().BeTrue("the study sheet is the study");
        window.FindDeep<Grid>("PhoneMovesSheet")!.IsVisible.Should().BeFalse();
        shell.ToggleSheetCommand.Execute(PhoneSheet.Analysis);
        shell.Main.Study.IsActive.Should().BeFalse();
        shell.Main.Analysis.IsAnalysisOn.Should().BeTrue();

        shell.Main.Study.ToggleCommand.Execute(null); // e.g. a shared study opened
        shell.Sheet.Should().Be(PhoneSheet.Study);
        shell.Main.Analysis.IsAnalysisOn.Should().BeFalse();

        shell.Back().Should().BeTrue();
        shell.Sheet.Should().Be(PhoneSheet.None);
        shell.Main.Study.IsActive.Should().BeFalse();
        shell.Back().Should().BeFalse("nothing left to close: Android leaves the app");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Windows_open_as_pages_and_dialogs_as_a_card()
    {
        var settings = new TestSettings();
        var preferences = new PreferencesViewModel(new ThemeService(settings), settings);
        var navigation = new PhoneNavigation(preferences);
        (Window window, PhoneShellViewModel shell) = Phone(navigation: navigation);

        shell.ToggleSheetCommand.Execute(PhoneSheet.Menu);
        Press(window.FindDeep<Button>("MenuPreferences")!);
        await Task.Delay(20);
        navigation.Page.Should().BeOfType<PreferencesView>();
        shell.Sheet.Should().Be(PhoneSheet.None, "choosing from the menu closes it");
        window.FindDeep<DockPanel>("PhonePage")!.IsVisible.Should().BeTrue();

        preferences.KataGoExecutable = "/data/katago";
        shell.Back().Should().BeTrue();
        navigation.HasPage.Should().BeFalse();
        settings.Current.KataGoExecutable.Should().Be("/data/katago", "back keeps what was typed, like Done");

        Task<bool> answer = navigation.ConfirmAsync("Sin guardar", "¿Descartar la partida?");
        window.FindDeep<Border>("PhoneMessage")!.IsVisible.Should().BeTrue();
        navigation.YesCommand.Execute(null);
        (await answer).Should().BeTrue();

        answer = navigation.ConfirmAsync("Sin guardar", "¿Descartar?");
        shell.Back().Should().BeTrue();
        (await answer).Should().BeFalse("back is no");

        var info = new GameInfoViewModel();
        Task<bool> edited = navigation.EditGameInfoAsync(info);
        navigation.Page.Should().BeOfType<GameInfoView>();
        Press(window.FindDeep<Button>("OkButton")!);
        (await edited).Should().BeTrue();
        navigation.HasPage.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Tapping_the_board_twice_plays_on_the_phone()
    {
        var game = new GameViewModel();
        (Window window, _) = Phone(game);
        game.ConfirmMoves = true; // the phone's default (the tests run on the desktop)
        GoBoardControl board = window.FindDeep<GoBoardControl>("Board")!;
        Avalonia.Point at = board.TranslatePoint(board.CurrentGeometry!.Value.Center(new Point(15, 3)), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        board.PendingPoint.Should().Be(new Point(15, 3));
        board.PendingStone.Should().Be(Stone.Black);
        game.MoveNumber.Should().Be(0);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        game.MoveNumber.Should().Be(1);
        board.PendingPoint.Should().BeNull();
        window.Close();
    }

    [Fact]
    public void The_phone_host_resolves_pages_and_dialogs_instead_of_windows()
    {
        ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddAppServices()
            .AddOgs(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddPhoneShell()
            .BuildServiceProvider(validateScopes: true);

        var navigation = services.GetRequiredService<PhoneNavigation>();
        services.GetRequiredService<IPreferencesWindowService>().Should().BeSameAs(navigation);
        services.GetRequiredService<ILobbyWindowService>().Should().BeSameAs(navigation);
        services.GetRequiredService<IReplaysWindowService>().Should().BeSameAs(navigation);
        services.GetRequiredService<IEngineWindows>().Should().BeSameAs(navigation);
        services.GetRequiredService<IDialogService>().Should().BeSameAs(navigation);
        services.GetRequiredService<PhoneShellViewModel>().Main.Should().BeSameAs(services.GetRequiredService<MainWindowViewModel>());
    }

    [AvaloniaFact]
    public async Task Phone_screenshots_are_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        try
        {
            ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
            var game = new GameViewModel { ConfirmMoves = true };
            foreach ((int x, int y) in new[] { (15, 3), (3, 15), (16, 15), (3, 3), (14, 16), (2, 5), (5, 2), (16, 9), (9, 15) })
            {
                game.PlayCommand.Execute(new Point(x, y));
                game.PlayCommand.Execute(new Point(x, y));
            }

            game.Tree.Info.BlackPlayer = "Ana";
            game.Tree.Info.WhitePlayer = "KataGo";
            var engine = new FakeAnalysisEngine { BestMove = new Point(13, 2) };
            (Window window, PhoneShellViewModel shell) = Phone(game, engine: engine, width: 412, height: 892);
            game.ConfirmMoves = true;
            game.PlayCommand.Execute(new Point(13, 2));
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "phone-board.png"));
            }

            shell.ToggleSheetCommand.Execute(PhoneSheet.Analysis);
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "phone-analysis.png"));
            }

            shell.ToggleSheetCommand.Execute(PhoneSheet.Menu);
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "phone-menu.png"));
            }

            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }
}
