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

        vm.Analysis.AtariGroups.Should().NotBeEmpty("the black stone at D16 is in atari");
        vm.Effects.CycleCommand.Execute(null);
        vm.Effects.CycleCommand.Execute(null);
        vm.Analysis.IsAtariAlertActive.Should().BeFalse();
        vm.Analysis.AtariGroups.Should().BeEmpty();
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
    public void Atari_trembles_only_with_full_effects()
    {
        (GoBoardControl board, Window window) = Board();
        board.Board = BoardState.Create(19).Setup([(new Point(3, 3), Stone.Black), (new Point(3, 2), Stone.White), (new Point(2, 3), Stone.White), (new Point(4, 3), Stone.White)]);
        board.AtariGroups = Atari.Groups(board.Board);
        board.AtariTime = 1.45; // inside this group's shiver (its phase comes from the liberty at D15)
        foreach ((EffectsLevel level, int trembling) in new[] { (EffectsLevel.Full, 1), (EffectsLevel.Subtle, 0), (EffectsLevel.Off, 0) })
        {
            board.Effects = level;
            board.InvalidateVisual();
            window.CaptureRenderedFrame()?.Dispose();
            board.TremblingStones.Should().Be(trembling, level.ToString());
        }

        window.Close();
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
