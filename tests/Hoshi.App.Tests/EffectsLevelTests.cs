using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class EffectsLevelTests
{
    [Fact]
    public void The_level_cycles_with_F_is_saved_and_announced()
    {
        var settings = new TestSettings();
        var effects = new EffectsViewModel(settings);
        effects.Level.Should().Be(EffectsLevel.Full, "full effects by default");
        int changes = 0;
        effects.Changed += (_, _) => changes++;

        effects.CycleCommand.Execute(null);
        effects.Level.Should().Be(EffectsLevel.Subtle);
        settings.Current.Effects.Should().Be(EffectsLevel.Subtle);
        effects.CycleCommand.Execute(null);
        effects.IsOff.Should().BeTrue();
        effects.Tooltip.Should().Be("Efectos visuales: Apagados (F)");
        effects.CycleCommand.Execute(null);
        effects.IsFull.Should().BeTrue();
        changes.Should().Be(3);

        effects.IsSubtle = true;
        effects.Level.Should().Be(EffectsLevel.Subtle);
        effects.IsFull = false; // unchecking a radio button does nothing by itself
        effects.Level.Should().Be(EffectsLevel.Subtle);

        settings.Save(settings.Current with { Effects = EffectsLevel.Off });
        effects.Refresh();
        effects.IsOff.Should().BeTrue();
    }

    [Fact]
    public void Turning_effects_off_mid_game_also_quiets_the_atari_alert_and_says_so()
    {
        var settings = new TestSettings();
        var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher(), settings: settings);
        foreach (Point p in new[] { new Point(3, 3), new Point(3, 2), new Point(10, 10), new Point(2, 3), new Point(10, 11), new Point(4, 3) })
        {
            vm.Game.PlayCommand.Execute(p);
        }

        vm.Analysis.GroupStatuses.Should().Contain(g => g.Health == GroupHealth.Critical, "the black stone at D16 is in atari");
        vm.Effects.CycleCommand.Execute(null);
        vm.Effects.CycleCommand.Execute(null);
        vm.Analysis.IsAtariAlertActive.Should().BeFalse();
        vm.Analysis.GroupStatuses.Should().BeEmpty();
        vm.Game.StatusText.Should().Be("Efectos visuales: Apagados");
    }

    private static (GoBoardControl Board, Window Window) Board()
    {
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board };
        var window = new Window { Width = 600, Height = 600, Content = board };
        window.Show();
        board.Board = BoardState.Create(19).Setup([(new Point(9, 9), Stone.Black)]);
        board.LastMove = new Point(9, 9);
        return (board, window);
    }

    [AvaloniaFact]
    public void Off_draws_no_impact_subtle_draws_a_small_one_and_turning_down_stops_a_running_explosion()
    {
        (GoBoardControl board, Window window) = Board();
        board.Effects = EffectsLevel.Off;
        board.Impact = new BoardImpact(new Point(9, 9), 3, 1);
        board.IsImpactRunning.Should().BeFalse();

        board.Effects = EffectsLevel.Subtle;
        board.Impact = new BoardImpact(new Point(9, 9), 3, 2);
        board.RunningImpactStrength.Should().Be(1, "subtle: only the small flash and ring, no shake or cracks");
        board.ImpactTime = 0.08;
        board.InvalidateVisual();
        using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "impact-subtle.png"));
        }

        board.ImpactTime = null;
        board.Effects = EffectsLevel.Full;
        board.Impact = new BoardImpact(new Point(9, 9), 3, 3);
        board.RunningImpactStrength.Should().Be(3);
        board.Effects = EffectsLevel.Subtle;
        board.IsImpactRunning.Should().BeFalse("turned down mid-explosion: it stops at once");
        window.Close();
    }

    [AvaloniaFact]
    public void Captures_fade_when_subtle_and_simply_vanish_when_off()
    {
        BoardState before = BoardState.Create(19).Setup([
            (new Point(3, 3), Stone.White), (new Point(2, 3), Stone.Black), (new Point(4, 3), Stone.Black), (new Point(3, 2), Stone.Black)]);
        BoardState after = before.TryPlay(Stone.Black, new Point(3, 4)).State!;
        foreach ((EffectsLevel level, int stones, bool subtle) in new[] { (EffectsLevel.Subtle, 1, true), (EffectsLevel.Off, 0, false) })
        {
            var board = new GoBoardControl { Animate = true, Effects = level };
            var window = new Window { Width = 400, Height = 400, Content = board };
            window.Show();
            board.Board = before;
            board.Board = after;
            board.LastMove = new Point(3, 4);
            board.CapturingStones.Should().Be(stones);
            board.IsCaptureSubtle.Should().Be(subtle);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_effects_button_and_the_sound_panel_offer_the_three_levels()
    {
        ThemeService.Apply(HoshiThemes.NightSky, animations: false, Avalonia.Application.Current!.Resources);
        var settings = new TestSettings();
        var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher(), settings: settings, audio: new AudioViewModel(settings));
        var window = new MainWindow(vm) { Width = 1100, Height = 720 };
        window.Show();
        Button button = window.FindControl<Button>("EffectsButton")!;
        button.Flyout!.ShowAt(button);
        await Task.Delay(80);
        using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "effects-panel.png"));
        }

        button.Flyout.Hide();
        vm.Effects.IsSubtle = true;
        window.FindControl<GoBoardControl>("Board")!.Effects.Should().Be(EffectsLevel.Subtle);
        settings.Current.Effects.Should().Be(EffectsLevel.Subtle);

        Button audio = window.FindControl<Button>("AudioButton")!;
        audio.Flyout!.ShowAt(audio);
        await Task.Delay(80);
        using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "audio-panel-effects.png"));
        }

        audio.Flyout.Hide();
        window.Close();
        ThemeService.Apply(HoshiThemes.Default, animations: false, Avalonia.Application.Current!.Resources);
    }
}

public sealed class WeakGroupHaloTests
{
    [AvaloniaFact]
    public void Halos_follow_the_engines_ownership_and_are_rendered()
    {
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board, ShowCoordinates = true };
        var window = new Window { Width = 640, Height = 640, Content = board };
        window.Show();
        // Black's solid corner (top left), White's settled corner (top right), a contested white group in the
        // centre, an unsettled black extension and a black stone that is lost inside White's area.
        (int X, int Y, Stone S)[] stones =
        [
            (2, 3, Stone.Black), (3, 2, Stone.Black), (3, 3, Stone.Black),
            (15, 3, Stone.White), (16, 2, Stone.White), (16, 4, Stone.White),
            (9, 9, Stone.White), (9, 10, Stone.White), (10, 9, Stone.White),
            (2, 9, Stone.Black), (2, 11, Stone.Black), (4, 10, Stone.White),
            (15, 2, Stone.Black),
        ];
        BoardState state = BoardState.Create(19).Setup([.. stones.Select(s => (new Point(s.X, s.Y), s.S))]);
        double[] own = new double[361];
        foreach ((int x, int y, Stone s) in stones)
        {
            own[(y * 19) + x] = (x, y) switch
            {
                ( < 4, < 4) => 0.92,
                (15 or 16, < 5) when s == Stone.White => -0.9,
                (15, 2) => -0.75,
                (9 or 10, _) => -0.05,
                (4, 10) => -0.9,
                _ => 0.45,
            };
        }

        board.Board = state;
        board.GroupStatuses = GroupStrength.Assess(state, own);
        board.GroupStatuses.Select(g => g.Health).Should().BeEquivalentTo([GroupHealth.Weak, GroupHealth.Unsettled, GroupHealth.Unsettled, GroupHealth.Critical]);
        board.GroupPulseTime = 1.3;
        board.InvalidateVisual();
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "weak-groups-ownership.png"));

        board.Effects = EffectsLevel.Off;
        board.InvalidateVisual();
        window.CaptureRenderedFrame()?.Dispose();
        window.Close();
    }
}
