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

    // On the headless UI thread: these build view models that touch Avalonia (themes, dispatcher). Built on a
    // test-pool thread first, they can bind Avalonia's UI dispatcher to that thread and break later UI tests.
    [AvaloniaFact]
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

    // On the headless UI thread: these build view models that touch Avalonia (themes, dispatcher). Built on a
    // test-pool thread first, they can bind Avalonia's UI dispatcher to that thread and break later UI tests.
    [AvaloniaFact]
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

    [AvaloniaFact]
    public void Phones_judge_moves_with_fewer_visits_and_do_not_wait_long()
    {
        bool was = PlatformInfo.IsPhone;
        try
        {
            PlatformInfo.IsPhone = true;
            var phone = new AnalysisViewModel(new GameViewModel(), ui: new ImmediateDispatcher());
            phone.JudgeThreshold.Should().Be(AnalysisViewModel.PhoneJudgeVisits);
            phone.JudgeFallback.Should().NotBeNull();
            PlatformInfo.IsPhone = false;
            var desktop = new AnalysisViewModel(new GameViewModel(), ui: new ImmediateDispatcher());
            desktop.JudgeThreshold.Should().Be(AnalysisViewModel.JudgeVisits);
            desktop.JudgeFallback.Should().BeNull();
        }
        finally
        {
            PlatformInfo.IsPhone = was;
        }
    }

    [AvaloniaFact]
    public void The_sheet_is_resized_by_its_handle_and_closes_when_dragged_away()
    {
        var settings = new TestSettings();
        var navigation = new PhoneNavigation();
        var main = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher(), dialogs: navigation, preferencesWindow: navigation);
        var shell = new PhoneShellViewModel(main, navigation, settings);
        var window = new Window { Width = 400, Height = 860, Content = new PhoneView { DataContext = shell } };
        window.Show();
        shell.ToggleSheetCommand.Execute(PhoneSheet.Moves);
        window.UpdateLayout();
        Border sheet = window.FindDeep<Border>("PhoneSheet")!;
        sheet.Bounds.Height.Should().Be(PhoneShellViewModel.DefaultSheetHeight);

        Border handle = window.FindDeep<Border>("PhoneSheetHandle")!;
        Avalonia.Point at = handle.TranslatePoint(new Avalonia.Point(handle.Bounds.Width / 2, 10), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at.WithY(at.Y - 150), RawInputModifiers.LeftMouseButton);
        window.MouseUp(at.WithY(at.Y - 150), MouseButton.Left);
        window.UpdateLayout();
        shell.SheetHeight.Should().BeApproximately(480, 1);
        sheet.Bounds.Height.Should().BeApproximately(480, 1);
        settings.Current.PhoneSheetHeight.Should().Be(480, "the height is kept for next time");

        shell.DragSheet(5000, 860);
        shell.SheetHeight.Should().Be(860 - PhoneShellViewModel.MinBoardRoom, "the board keeps some room");

        // Dragged almost away: the sheet closes and opens again at its last height.
        shell.DragSheet(30, 860);
        shell.EndSheetDrag(480);
        shell.Sheet.Should().Be(PhoneSheet.None);
        shell.SheetHeight.Should().Be(480);
        new PhoneShellViewModel(main, navigation, settings).SheetHeight.Should().Be(480);
        window.Close();
    }

    [AvaloniaFact]
    public void A_swipe_on_the_header_steps_through_the_game()
    {
        var game = new GameViewModel();
        (Window window, _) = Phone(game);
        foreach ((int x, int y) in new[] { (3, 3), (15, 15), (3, 15), (15, 3), (9, 9) })
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        Border top = window.FindDeep<Border>("PhoneTop")!;
        Avalonia.Point start = top.TranslatePoint(new Avalonia.Point(300, top.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start.WithX(start.X - (3 * PhoneView.SwipeStep) - 2), RawInputModifiers.LeftMouseButton);
        game.MoveNumber.Should().Be(2, "a swipe to the left goes back one move per step");
        window.MouseMove(start.WithX(start.X - PhoneView.SwipeStep + 2), RawInputModifiers.LeftMouseButton);
        game.MoveNumber.Should().Be(4, "and back to the right goes forward");
        window.MouseUp(start, MouseButton.Left);
        window.Close();
    }

    [AvaloniaFact]
    public void Stones_buzz_and_the_screen_stays_on_during_a_game()
    {
        int buzzes = 0;
        var awake = new List<bool>();
        Action? vibrate = PlatformInfo.Vibrate;
        Action<bool>? keep = PlatformInfo.KeepScreenOn;
        try
        {
            PlatformInfo.Vibrate = () => buzzes++;
            PlatformInfo.KeepScreenOn = awake.Add;
            var settings = new TestSettings();
            var navigation = new PhoneNavigation();
            var game = new GameViewModel();
            var main = new MainWindowViewModel(game, ui: new ImmediateDispatcher(), dialogs: navigation, preferencesWindow: navigation);
            var shell = new PhoneShellViewModel(main, navigation, settings);
            shell.KeepAwake.Should().BeFalse();

            game.PlayCommand.Execute(new Point(3, 3));
            buzzes.Should().Be(1);
            shell.KeepAwake.Should().BeTrue();
            awake.Should().Equal(true);

            settings.Save(settings.Current with { Haptics = false });
            game.PlayCommand.Execute(new Point(15, 15));
            buzzes.Should().Be(1, "vibration can be switched off");

            game.GoFirstCommand.Execute(null);
            awake.Should().Equal(true, false);
        }
        finally
        {
            PlatformInfo.Vibrate = vibrate;
            PlatformInfo.KeepScreenOn = keep;
        }
    }
}
