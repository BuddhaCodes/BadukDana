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
using Hoshi.Ogs;
using Hoshi.Ogs.Games;
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

    [AvaloniaFact]
    public void Turned_sideways_the_sheet_goes_to_the_right_of_the_board()
    {
        (Window window, PhoneShellViewModel shell) = Phone(width: 1280, height: 800);
        window.UpdateLayout();
        shell.IsLandscape.Should().BeTrue();
        shell.ToggleSheetCommand.Execute(PhoneSheet.Moves);
        window.UpdateLayout();
        Border sheet = window.FindDeep<Border>("PhoneSheet")!;
        sheet.Bounds.Width.Should().BeApproximately(512, 1);
        sheet.Bounds.Height.Should().BeGreaterThan(500, "the sheet takes the full height beside the board");
        GoBoardControl board = window.FindDeep<GoBoardControl>("Board")!;
        board.Bounds.Right.Should().BeLessThanOrEqualTo(sheet.Bounds.Left + 1);
        window.FindDeep<Border>("PhoneSheetHandle")!.IsVisible.Should().BeFalse();

        shell.UpdateSize(400, 860); // turned back (what the view reports when it is resized)
        window.UpdateLayout();
        shell.IsLandscape.Should().BeFalse();
        sheet.Bounds.Height.Should().BeApproximately(shell.SheetHeight, 1);
        window.Close();
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
        shell.Back().Should().BeTrue("the board goes back to the home screen");
        shell.IsHome.Should().BeTrue();
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
        services.GetRequiredService<ITsumegoWindowService>().Should().BeSameAs(navigation);
        services.GetRequiredService<GameViewModel>().Should().NotBeNull("the game's dialogs are the navigation, which opens problems lazily");
        services.GetRequiredService<PhoneShellViewModel>().Main.Should().BeSameAs(services.GetRequiredService<MainWindowViewModel>());
    }

    [AvaloniaFact]
    public async Task Phone_screenshots_are_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        bool wasPhone = PlatformInfo.IsPhone;
        PlatformInfo.IsPhone = true;
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

            shell.ToggleSheetCommand.Execute(PhoneSheet.Study);
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "phone-study.png"));
            }

            shell.ToggleSheetCommand.Execute(PhoneSheet.Moves);
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "phone-moves.png"));
            }

            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            PlatformInfo.IsPhone = wasPhone;
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
    public void The_home_screen_continues_the_game_starts_new_ones_and_opens_the_modes()
    {
        var game = new GameViewModel();
        var navigation = new PhoneNavigation();
        (Window window, PhoneShellViewModel shell) = Phone(game, navigation);
        shell.ShowHomeCommand.Execute(null);
        window.FindDeep<DockPanel>("PhoneHome")!.IsVisible.Should().BeTrue();
        window.FindDeep<Button>("HomeContinue")!.IsVisible.Should().BeFalse("nothing on the board yet");

        Press(window.FindDeep<Button>("HomeNew19")!);
        shell.IsHome.Should().BeFalse();
        game.PlayCommand.Execute(new Point(3, 3));
        game.PlayCommand.Execute(new Point(15, 15));

        shell.ToggleSheetCommand.Execute(PhoneSheet.Menu);
        Press(window.FindDeep<Button>("MenuHome")!);
        shell.IsHome.Should().BeTrue();
        shell.Sheet.Should().Be(PhoneSheet.None);
        shell.HasGameToContinue.Should().BeTrue();
        window.FindDeep<Button>("HomeContinue")!.IsVisible.Should().BeTrue();
        shell.ContinueDetail.Should().Contain("2");
        Save(window, "phone-home.png");

        Press(window.FindDeep<Button>("HomeContinue")!);
        shell.IsHome.Should().BeFalse();
        game.MoveNumber.Should().Be(2, "continuing keeps the game");
        window.FindDeep<Border>("PhoneBlackCard")!.Classes.Should().Contain("turn");
        window.FindDeep<Border>("PhoneWhiteCard")!.Classes.Should().NotContain("turn");
        Save(window, "phone-play.png");

        shell.ShowHomeCommand.Execute(null);
        Press(window.FindDeep<Button>("HomeTileStudy")!);
        shell.IsHome.Should().BeFalse();
        shell.Sheet.Should().Be(PhoneSheet.Study);
        shell.Main.Study.IsActive.Should().BeTrue();
        window.FindDeep<Grid>("PhoneStudyNav")!.IsVisible.Should().BeTrue("studying shows the position bar");
        shell.Main.Moves.LineLength.Should().Be(2);
        shell.Main.Moves.Position = 1;
        game.MoveNumber.Should().Be(1, "dragging the bar moves through the game");
        shell.Main.Moves.PositionText.Should().Be("1 / 2");
        Save(window, "phone-study.png");
        window.Close();
    }

    [AvaloniaFact]
    public void Online_the_opponent_is_above_the_board_you_below_and_a_tap_waits_for_Confirmar()
    {
        var game = new GameViewModel { ConfirmMoves = true };
        (Window window, PhoneShellViewModel shell) = Phone(game);
        var fake = new FakeOnlineGame(70000001, 1001);
        var online = new OnlineGameViewModel(fake, game, new ImmediateDispatcher());
        online.Connect();
        fake.Gamedata(new OgsGameSnapshot
        {
            GameId = 70000001,
            Width = 9,
            Height = 9,
            Komi = 6.5,
            Black = new OgsUser(1001, "kuro_test", 25.4, false),
            White = new OgsUser(2002, "shiro_test", 27.2, false),
            TimeControl = new OgsTimeControl { System = "byoyomi", MainTime = 600, PeriodTime = 30, Periods = 5 },
        });
        shell.Main.Online = online;
        game.ConfirmMoves = true; // the phone's default (the tests run on the desktop)

        shell.TopCard.Should().NotBeNull();
        shell.TopCard!.Name.Should().Contain("shiro_test", "the opponent is on top");
        shell.TopCard.IsBlack.Should().BeFalse();
        shell.BottomCard!.Should().Match<PlayerCard>(c => c.Name.Contains("kuro_test") && c.IsYou && c.IsBlack && c.IsToMove);
        window.FindDeep<Border>("PhoneBottomCard")!.Classes.Should().Contain("turn");
        window.FindDeep<Border>("PhoneTopCard")!.Classes.Should().NotContain("turn");
        window.FindDeep<StackPanel>("PhoneOnlineBottom")!.IsVisible.Should().BeTrue();

        online.IsMyTurn.Should().BeTrue();
        online.CanPlayAt(new Point(6, 2)).Should().BeTrue();
        game.Online.Should().BeSameAs(online);
        game.PlayCommand.Execute(new Point(6, 2));
        game.PendingMove.Should().Be(new Point(6, 2));
        window.FindDeep<Border>("PhoneConfirmBar")!.IsVisible.Should().BeTrue();
        game.ConfirmPendingLabel.Should().Be("Confirmar G7");
        Save(window, "phone-ogs.png");

        game.CancelPendingCommand.Execute(null);
        game.HasPendingMove.Should().BeFalse();
        window.FindDeep<Border>("PhoneConfirmBar")!.IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_study_sheet_shows_a_quick_read_when_KataGo_may_be_used()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(4, 4) };
        var game = new GameViewModel();
        (Window window, PhoneShellViewModel shell) = Phone(game, engine: engine);
        game.PlayCommand.Execute(new Point(2, 2));
        shell.ToggleSheetCommand.Execute(PhoneSheet.Study);
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(20);
        }

        shell.Main.Analysis.HasQuickRead.Should().BeTrue();
        shell.Main.Analysis.BestMoveText.Should().Be("E15");
        window.FindDeep<Avalonia.Controls.Primitives.UniformGrid>("StudyQuickRead")!.IsVisible.Should().BeTrue();
        window.Close();
    }

    private static void Save(Window window, string name)
    {
        using Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
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

    [AvaloniaFact]
    public void Study_drawings_on_the_phone_wait_for_save()
    {
        var game = new GameViewModel();
        (Window window, PhoneShellViewModel shell) = Phone(game);
        game.ConfirmMoves = true; // the phone's default (the tests run on the desktop)
        game.PlayCommand.Execute(new Point(3, 3));
        game.PlayCommand.Execute(new Point(3, 3));
        shell.ToggleSheetCommand.Execute(PhoneSheet.Study);
        shell.Main.Study.SelectToolCommand.Execute(Hoshi.App.ViewModels.StudyTool.Arrow);
        window.FindDeep<Border>("StudyPending")!.IsVisible.Should().BeFalse();

        GoBoardControl board = window.FindDeep<GoBoardControl>("Board")!;
        window.UpdateLayout(); // the study sheet makes the board smaller
        Avalonia.Point At(int x, int y) => board.TranslatePoint(board.CurrentGeometry!.Value.Center(new Point(x, y)), window)!.Value;
        window.MouseDown(At(10, 10), MouseButton.Left);
        window.MouseUp(At(10, 10), MouseButton.Left);
        window.MouseDown(At(13, 10), MouseButton.Left);
        window.MouseUp(At(13, 10), MouseButton.Left);

        board.StudyDrawings.Should().ContainSingle(d => d.Kind == Hoshi.Sgf.Study.DrawingKind.Arrow);
        board.PendingDrawingIds.Should().ContainSingle();
        window.FindDeep<Border>("StudyPending")!.IsVisible.Should().BeTrue();
        Press(window.FindDeep<Button>("StudyPendingSave")!);
        window.FindDeep<Border>("StudyPending")!.IsVisible.Should().BeFalse();
        Hoshi.Sgf.Study.StudyStore.Read(game.CurrentNode).Drawings.Should().ContainSingle();
        window.Close();
    }

    [AvaloniaFact]
    public void Phones_analyse_with_fewer_visits_by_default()
    {
        bool was = PlatformInfo.IsPhone;
        try
        {
            PlatformInfo.IsPhone = true;
            PlatformInfo.AnalysisVisits(new AppSettings()).Should().Be(12);
            PlatformInfo.AnalysisVisits(new AppSettings { AnalysisVisits = 300 }).Should().Be(300);
            PlatformInfo.IsPhone = false;
            PlatformInfo.AnalysisVisits(new AppSettings()).Should().Be(200);
        }
        finally
        {
            PlatformInfo.IsPhone = was;
        }
    }

    [AvaloniaFact]
    public void Preferences_on_a_phone_scroll_as_a_whole_and_stack_the_board_tab()
    {
        bool was = PlatformInfo.IsPhone;
        try
        {
            PlatformInfo.IsPhone = true;
            var settings = new TestSettings();
            var view = new PreferencesView { DataContext = new PreferencesViewModel(new ThemeService(settings), settings) };
            var window = new Window { Width = 412, Height = 892, Content = view };
            window.Show();
            view.Classes.Should().Contain("phone");
            window.FindDeep<ScrollViewer>("AppearanceScroll")!.VerticalScrollBarVisibility.Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            window.FindDeep<ListBox>("ThemeList")!.Bounds.Height.Should().BeGreaterThan(500, "every theme card is shown, two per row");
            window.Close();
        }
        finally
        {
            PlatformInfo.IsPhone = was;
        }
    }
}
