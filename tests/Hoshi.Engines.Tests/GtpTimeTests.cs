using Hoshi.Core;
using Hoshi.Engines.Gtp;

namespace Hoshi.Engines.Tests;

/// <summary>The clock in engine games: the settings each engine understands and time_left before every genmove.</summary>
public sealed class GtpTimeTests
{
    private static readonly Func<string, bool> Plain = _ => false;
    private static readonly Func<string, bool> KataGo = c => c is "kata-time_settings" or "kgs-time_settings";

    [Fact]
    public void Settings_use_the_richest_command_the_engine_lists()
    {
        TimeControl fischer = TimeControl.Fischer(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));
        TimeControl byo = TimeControl.ByoYomi(TimeSpan.FromMinutes(10), 5, TimeSpan.FromSeconds(30));
        TimeControl absolute = TimeControl.Absolute(TimeSpan.FromMinutes(20));

        GtpTimeCommands.Settings(fischer, KataGo).Should().Be("kata-time_settings fischer 300 5");
        GtpTimeCommands.Settings(fischer with { MaxTime = TimeSpan.FromMinutes(10) }, KataGo).Should().Be("kata-time_settings fischer-capped 300 5 600 -1");
        GtpTimeCommands.Settings(fischer, Plain).Should().Be("time_settings 300 0 0");
        GtpTimeCommands.Settings(byo, KataGo).Should().Be("kgs-time_settings byoyomi 600 30 5");
        GtpTimeCommands.Settings(byo, Plain).Should().Be("time_settings 600 30 1");
        GtpTimeCommands.Settings(absolute, KataGo).Should().Be("time_settings 1200 0 0");
    }

    [Fact]
    public void Time_left_reports_main_time_then_the_period_and_periods_in_overtime()
    {
        TimeControl byo = TimeControl.ByoYomi(TimeSpan.FromMinutes(1), 3, TimeSpan.FromSeconds(30));
        var clock = new GameClock(byo);

        GtpTimeCommands.TimeLeft(Stone.White, GtpTime.Of(clock, Stone.White), "kgs-time_settings byoyomi 60 30 3").Should().Be("time_left W 60 0");

        clock.Elapse(Stone.Black, TimeSpan.FromSeconds(75)); // main time gone, 15 s into the first period
        GtpTime black = GtpTime.Of(clock, Stone.Black);
        GtpTimeCommands.TimeLeft(Stone.Black, black, "kgs-time_settings byoyomi 60 30 3").Should().Be("time_left B 15 3");
        GtpTimeCommands.TimeLeft(Stone.Black, black, "time_settings 60 30 1").Should().Be("time_left B 15 1");
    }

    [Fact]
    public async Task The_engine_hears_the_settings_once_and_time_left_before_each_move_then_no_limit()
    {
        var process = new FakeGtpProcess(c => c.Split(' ')[0] switch
        {
            "protocol_version" => "= 2",
            "list_commands" => "= play\ngenmove\nundo\nkata-time_settings\nkgs-time_settings",
            "genmove" => "= Q16",
            _ => "=",
        });
        await using var engine = await GtpEngine.ConnectAsync(new GtpClient(process), new GtpEngineConfig("Test", "/x/engine", "", ""));
        var clock = new GameClock(TimeControl.Fischer(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5)));
        var position = new GtpPosition { Width = 19, Height = 19 };

        int before = process.Commands().Length;
        await engine.GenMoveAsync(position, GtpTime.Of(clock, Stone.Black));
        process.Commands()[before..].Should().ContainInOrder("kata-time_settings fischer 300 5", "time_left B 300 0", "genmove B");

        clock.Elapse(Stone.Black, TimeSpan.FromSeconds(42));
        before = process.Commands().Length;
        await engine.GenMoveAsync(position with { Moves = [], ToMove = Stone.Black }, GtpTime.Of(clock, Stone.Black));
        process.Commands()[before..].Should().NotContain(c => c.StartsWith("kata-time_settings", StringComparison.Ordinal));
        process.Commands()[before..].Should().Contain("time_left B 258 0");

        before = process.Commands().Length;
        await engine.GenMoveAsync(position);
        process.Commands()[before..].Should().Contain(GtpTimeCommands.NoLimit).And.NotContain(c => c.StartsWith("time_left", StringComparison.Ordinal));
    }
}
