using Hoshi.App.Controls;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>The 2026-10 redesign on the desktop: the review inspector, the floating study palette and the Kaya theme.</summary>
public sealed class RedesignTests
{
    private static readonly (int X, int Y)[] Moves =
        [(3, 3), (15, 15), (15, 3), (3, 15), (2, 5), (16, 13), (5, 2), (13, 16), (9, 3), (9, 15), (10, 10), (8, 8)];

    private static async Task SettleAsync()
    {
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(20);
        }
    }

    private static void Save(Window window, string name)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
    }

    [AvaloniaFact]
    public async Task The_review_inspector_shows_candidates_and_mistakes_and_is_saved_as_screenshot()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(16, 3), SecondGap = 9, MissFactor = 4 };
        var vm = new MainWindowViewModel(new GameViewModel(), engine: engine, ui: new ImmediateDispatcher());
        foreach ((int x, int y) in Moves)
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        var window = new MainWindow(vm) { Width = 1440, Height = 900 };
        window.Show();
        vm.Analysis.IsAnalysisOn = true;
        await SettleAsync();

        window.FindDeep<StackPanel>("ReviewCandidates")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindDeep<Border>("ReviewMistakes")!.IsEffectivelyVisible.Should().BeTrue();
        vm.Analysis.KeyMistakes.Should().HaveCount(3);
        window.FindDeep<Button>("NextMistakeButton")!.Command!.CanExecute(null).Should().BeTrue();
        Save(window, "review-inspector.png");
    }

    [AvaloniaFact]
    public async Task The_tabs_switch_between_play_study_and_review_and_review_shows_the_move_column()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(16, 3), SecondGap = 9, MissFactor = 4 };
        var vm = new MainWindowViewModel(new GameViewModel(), engine: engine, ui: new ImmediateDispatcher());
        foreach ((int x, int y) in Moves)
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        var window = new MainWindow(vm) { Width = 1440, Height = 900 };
        window.Show();
        vm.Mode.Should().Be(WorkMode.Play);
        window.FindDeep<Border>("MoveColumn")!.IsEffectivelyVisible.Should().BeFalse();

        vm.SetModeCommand.Execute(WorkMode.Study);
        vm.Study.IsActive.Should().BeTrue();
        vm.IsStudyMode.Should().BeTrue();
        window.FindDeep<RadioButton>("ModeStudy")!.IsChecked.Should().BeTrue();

        vm.SetModeCommand.Execute(WorkMode.Review);
        vm.Study.IsActive.Should().BeFalse("the study closes when reviewing");
        vm.Analysis.IsAnalysisOn.Should().BeTrue();
        await SettleAsync();
        window.FindDeep<Border>("MoveColumn")!.IsEffectivelyVisible.Should().BeTrue();
        vm.Moves.Rows.Should().HaveCount(6, "twelve moves, two per row");
        vm.Moves.Rows[0].First.Text.Should().Be("D16");
        vm.Moves.Rows.SelectMany(r => new[] { r.First, r.Second }).Count(c => c is { IsMistake: true } or { IsBlunder: true }).Should().BeGreaterThan(0);
        vm.Moves.CurrentRow.Should().BeSameAs(vm.Moves.Rows[^1]);
        Save(window, "review-mode.png");

        vm.Moves.GoToCommand.Execute(vm.Moves.Rows[1].Second);
        vm.Game.MoveNumber.Should().Be(4);
        vm.Moves.CurrentRow.Should().BeSameAs(vm.Moves.Rows[1]);

        vm.Study.ToggleCommand.Execute(null); // S on the keyboard
        vm.Mode.Should().Be(WorkMode.Study, "the tabs follow the study and the analysis however they change");
        vm.SetModeCommand.Execute(WorkMode.Play);
        (vm.Study.IsActive, vm.Analysis.IsAnalysisOn).Should().Be((false, false));

        window.Close();
        vm.SetModeCommand.Execute(WorkMode.Review);
        var narrow = new MainWindow(vm) { Width = 1000, Height = 800 };
        narrow.Show();
        narrow.FindDeep<Panel>("MoveColumnHost")!.IsVisible.Should().BeFalse("a narrow window keeps the room for the board");
        narrow.Close();
    }

    [AvaloniaFact]
    public void The_desktop_study_tools_float_over_the_board()
    {
        var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher());
        foreach ((int x, int y) in Moves)
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        var window = new MainWindow(vm) { Width = 1440, Height = 900 };
        window.Show();
        window.FindDeep<Border>("StudyPalette")!.IsVisible.Should().BeFalse();

        vm.Study.ToggleCommand.Execute(null);

        window.FindDeep<Border>("StudyPalette")!.IsVisible.Should().BeTrue();
        window.FindDeep<WrapPanel>("StudyTools")!.IsVisible.Should().BeFalse("on the desktop the tools are on the board");
        RadioButton arrow = window.FindDeep<RadioButton>("PaletteArrow")!;
        arrow.Command!.Execute(arrow.CommandParameter);
        vm.Study.Tool.Should().Be(StudyTool.Arrow);
        Button clear = window.FindDeep<Button>("PaletteClear")!;
        ((HoshiIcon)clear.Content!).Icon.Should().BeSameAs(Application.Current!.FindResource("Icon.Trash"),
            "clearing every drawing is a bin, not an \"x\" that reads as close");
        Save(window, "study-palette.png");

        vm.ToggleZenCommand.Execute(null);
        window.FindDeep<Border>("StudyPalette")!.IsVisible.Should().BeFalse("zen hides everything but the board");
    }

    [AvaloniaFact]
    public async Task The_kaya_theme_renders_light_and_is_saved_as_screenshot()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(16, 3), SecondGap = 9, MissFactor = 4 };
        var themes = new ThemeService();
        var vm = new MainWindowViewModel(new GameViewModel(), engine: engine, ui: new ImmediateDispatcher());
        foreach ((int x, int y) in Moves)
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        Avalonia.Styling.ThemeVariant? before = Application.Current!.RequestedThemeVariant;
        try
        {
            themes.Select(HoshiThemes.Kaya, animations: false);
            var window = new MainWindow(vm) { Width = 1440, Height = 900 };
            window.Show();
            vm.Analysis.IsAnalysisOn = true;
            await SettleAsync();

            window.ActualThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Light);
            Save(window, "theme-kaya.png");
        }
        finally
        {
            Application.Current.RequestedThemeVariant = before;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current.Resources);
        }
    }

    [AvaloniaFact]
    public async Task The_mochi_theme_is_the_default_and_is_saved_as_screenshot()
    {
        HoshiThemes.Default.Should().BeSameAs(HoshiThemes.Mochi);
        var engine = new FakeAnalysisEngine { BestMove = new Point(16, 3), SecondGap = 9, MissFactor = 4 };
        var vm = new MainWindowViewModel(new GameViewModel(), engine: engine, ui: new ImmediateDispatcher());
        foreach ((int x, int y) in Moves)
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        Avalonia.Styling.ThemeVariant? before = Application.Current!.RequestedThemeVariant;
        try
        {
            new ThemeService().Select(HoshiThemes.Mochi, animations: false);
            var window = new MainWindow(vm) { Width = 1440, Height = 900 };
            window.Show();
            vm.Analysis.IsAnalysisOn = true;
            await SettleAsync();
            vm.SetModeCommand.Execute(WorkMode.Review);
            await SettleAsync();

            window.ActualThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Light);
            Save(window, "theme-mochi.png");
            vm.SetModeCommand.Execute(WorkMode.Study);
            await SettleAsync();
            Save(window, "theme-mochi-study.png");
        }
        finally
        {
            Application.Current.RequestedThemeVariant = before;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current.Resources);
        }
    }
}
