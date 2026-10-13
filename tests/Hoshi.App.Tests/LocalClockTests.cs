using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>A time source the test moves by hand (its timers never fire by themselves).</summary>
internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Idle();

    private sealed class Idle : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

[Collection("Language")]
public sealed class LocalClockTests
{
    [AvaloniaFact]
    public void The_clock_runs_for_the_player_to_move_and_gives_byo_yomi_back_after_a_move()
    {
        var game = new GameViewModel();
        var time = new ManualTime();
        var clock = new LocalClockViewModel(game, new ImmediateDispatcher(), time);
        clock.StartCommand.Execute(new ClockPreset(TimeControl.ByoYomi(TimeSpan.FromSeconds(10), 3, TimeSpan.FromSeconds(30))));
        clock.HasClock.Should().BeTrue();
        clock.BlackText.Should().Be("0:10");

        time.Now += TimeSpan.FromSeconds(4);
        clock.Tick(time.Now);
        clock.BlackText.Should().Be("0:06");
        clock.WhiteText.Should().Be("0:10");

        time.Now += TimeSpan.FromSeconds(16); // Black thinks into byo-yomi, then plays
        game.PlayCommand.Execute(new Point(3, 3));
        clock.BlackText.Should().Be("0:30 ×3", "a move in time gives the period back");

        time.Now += TimeSpan.FromSeconds(3);
        clock.Tick(time.Now);
        clock.WhiteText.Should().Be("0:07");
        clock.BlackText.Should().Be("0:30 ×3", "only the player to move loses time");

        clock.TogglePauseCommand.Execute(null);
        time.Now += TimeSpan.FromMinutes(5);
        clock.Tick(time.Now);
        clock.WhiteText.Should().Be("0:07", "a paused clock stands still");
    }

    [AvaloniaFact]
    public void Running_out_of_time_loses_the_game()
    {
        var game = new GameViewModel();
        var time = new ManualTime();
        var clock = new LocalClockViewModel(game, new ImmediateDispatcher(), time);
        clock.StartCommand.Execute(new ClockPreset(TimeControl.Absolute(TimeSpan.FromSeconds(30))));
        time.Now += TimeSpan.FromSeconds(25);
        clock.Tick(time.Now);
        clock.IsBlackLow.Should().BeTrue();
        time.Now += TimeSpan.FromSeconds(6);
        clock.Tick(time.Now);
        game.Tree.Info.Result.Should().Be("W+T");
        clock.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_new_game_drops_the_clock_and_the_bar_shows_it()
    {
        var game = new GameViewModel();
        var vm = new MainWindowViewModel(game, ui: new ImmediateDispatcher());
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        vm.Clock.StartCommand.Execute(vm.Clock.Presets[1]);
        window.FindDeep<Border>("BlackClock")!.IsVisible.Should().BeTrue();
        game.NewGameCommand.Execute(19);
        vm.Clock.HasClock.Should().BeFalse();
        window.FindDeep<Border>("BlackClock")!.IsVisible.Should().BeFalse();
        window.Close();
    }
}
