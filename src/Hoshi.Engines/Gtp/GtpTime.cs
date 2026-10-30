using System.Globalization;
using Hoshi.Core;

namespace Hoshi.Engines.Gtp;

/// <summary>What the engine's player has left on the clock when it is asked for a move.</summary>
public sealed record GtpTime(TimeControl Control, TimeSpan Main, int Periods, TimeSpan Period, bool InOvertime)
{
    /// <summary>The state of <paramref name="color"/>'s clock.</summary>
    public static GtpTime Of(GameClock clock, Stone color)
    {
        ArgumentNullException.ThrowIfNull(clock);
        PlayerClock p = clock.For(color);
        return new GtpTime(clock.Control, p.Main, p.Periods, p.Period, p.InOvertime);
    }
}

/// <summary>
/// Turns Hoshi's clocks into GTP: the best <c>time_settings</c> the engine understands (KataGo's
/// <c>kata-time_settings</c> for Fischer, <c>kgs-time_settings</c> for real byo-yomi, plain GTP otherwise) and
/// the matching <c>time_left</c> before each <c>genmove</c>.
/// </summary>
public static class GtpTimeCommands
{
    /// <summary>GTP's way of saying "no time limit" (byo-yomi time &gt; 0 with 0 stones).</summary>
    public const string NoLimit = "time_settings 0 1 0";

    /// <summary>The settings command for <paramref name="control"/>, given which commands the engine supports.</summary>
    public static string Settings(TimeControl control, Func<string, bool> supports)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(supports);
        string main = Whole(control.MainTime);
        return control.System switch
        {
            TimeSystem.Fischer when supports("kata-time_settings") => control.MaxTime is { } max
                ? $"kata-time_settings fischer-capped {Exact(control.MainTime)} {Exact(control.Increment)} {Exact(max)} -1"
                : $"kata-time_settings fischer {Exact(control.MainTime)} {Exact(control.Increment)}",

            // Without the extension Fischer is told as absolute time; time_left keeps it honest move by move.
            TimeSystem.Fischer => $"time_settings {main} 0 0",
            TimeSystem.ByoYomi when control.Periods > 0 && supports("kgs-time_settings") =>
                $"kgs-time_settings byoyomi {main} {Whole(control.PeriodTime)} {control.Periods}",

            // Plain GTP only knows Canadian overtime: one stone per period is the same as one byo-yomi period.
            TimeSystem.ByoYomi when control.Periods > 0 => $"time_settings {main} {Whole(control.PeriodTime)} 1",
            _ => $"time_settings {main} 0 0",
        };
    }

    /// <summary>The <c>time_left</c> command for the settings sent with <see cref="Settings"/>.</summary>
    public static string TimeLeft(Stone color, GtpTime time, string settings)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(settings);
        string c = color == Stone.White ? "W" : "B";
        bool byoYomi = time.Control.System == TimeSystem.ByoYomi && time.Control.Periods > 0;
        if (!byoYomi || !time.InOvertime)
        {
            return $"time_left {c} {Whole(time.Main)} 0";
        }

        // In overtime: the current period's time, and (KGS) the periods left or (Canadian) the one stone to play.
        int stones = settings.StartsWith("kgs-", StringComparison.Ordinal) ? Math.Max(1, time.Periods) : 1;
        return $"time_left {c} {Whole(time.Period)} {stones}";
    }

    private static string Whole(TimeSpan t) => Math.Max(0, (long)Math.Floor(t.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static string Exact(TimeSpan t) => Math.Max(0, Math.Round(t.TotalSeconds, 1)).ToString("0.#", CultureInfo.InvariantCulture);
}
