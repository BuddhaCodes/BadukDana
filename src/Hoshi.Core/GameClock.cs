using System.Globalization;

namespace Hoshi.Core;

/// <summary>How a game is timed (local games and games against an engine; OGS has its own clock).</summary>
public enum TimeSystem
{
    /// <summary>One amount of time for the whole game.</summary>
    Absolute,

    /// <summary>Main time, then a number of periods of fixed length; a move in time keeps the period.</summary>
    ByoYomi,

    /// <summary>Main time plus an increment after every move (optionally capped).</summary>
    Fischer,
}

/// <summary>A time control: main time and, depending on the system, byo-yomi periods or a Fischer increment.</summary>
public sealed record TimeControl(TimeSystem System, TimeSpan MainTime, int Periods, TimeSpan PeriodTime, TimeSpan Increment, TimeSpan? MaxTime)
{
    public static TimeControl Absolute(TimeSpan main) => new(TimeSystem.Absolute, main, 0, TimeSpan.Zero, TimeSpan.Zero, null);

    public static TimeControl ByoYomi(TimeSpan main, int periods, TimeSpan periodTime) =>
        new(TimeSystem.ByoYomi, main, periods, periodTime, TimeSpan.Zero, null);

    public static TimeControl Fischer(TimeSpan main, TimeSpan increment, TimeSpan? maxTime = null) =>
        new(TimeSystem.Fischer, main, 0, TimeSpan.Zero, increment, maxTime);

    /// <summary>"10:00 + 5×30s", "5:00 + 5s" or "20:00".</summary>
    public string Describe() => System switch
    {
        TimeSystem.ByoYomi => $"{GameClock.Format(MainTime)} + {Periods}×{Seconds(PeriodTime)}",
        TimeSystem.Fischer => $"{GameClock.Format(MainTime)} + {Seconds(Increment)}",
        _ => GameClock.Format(MainTime),
    };

    private static string Seconds(TimeSpan t) => ((int)t.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
}

/// <summary>One player's time: what is left of the main time, the byo-yomi periods and the current period.</summary>
public sealed class PlayerClock
{
    internal PlayerClock(TimeControl control)
    {
        Main = control.MainTime;
        Periods = control.Periods;
        Period = control.PeriodTime;
    }

    public TimeSpan Main { get; internal set; }

    /// <summary>Byo-yomi periods left (the current one included).</summary>
    public int Periods { get; internal set; }

    /// <summary>What is left of the current byo-yomi period.</summary>
    public TimeSpan Period { get; internal set; }

    public bool IsFlagged { get; internal set; }

    /// <summary>In byo-yomi (main time used up).</summary>
    public bool InOvertime { get; internal set; }
}

/// <summary>
/// The clock of a local game: only the player to move loses time (<see cref="Elapse"/>), and a finished move
/// (<see cref="MoveDone"/>) resets the byo-yomi period or adds the Fischer increment. Pure logic: the app ticks it.
/// </summary>
public sealed class GameClock
{
    public GameClock(TimeControl control)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
        Black = new PlayerClock(control);
        White = new PlayerClock(control);
    }

    public TimeControl Control { get; }

    public PlayerClock Black { get; }

    public PlayerClock White { get; }

    public PlayerClock For(Stone color) => color == Stone.White ? White : Black;

    /// <summary>Time passes for <paramref name="color"/>; true when that player just ran out of time.</summary>
    public bool Elapse(Stone color, TimeSpan elapsed)
    {
        PlayerClock p = For(color);
        if (p.IsFlagged || elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        TimeSpan left = elapsed;
        if (p.Main > TimeSpan.Zero)
        {
            TimeSpan used = left < p.Main ? left : p.Main;
            p.Main -= used;
            left -= used;
            if (left <= TimeSpan.Zero && p.Main > TimeSpan.Zero)
            {
                return false;
            }
        }

        if (Control.System != TimeSystem.ByoYomi || Control.Periods <= 0)
        {
            if (p.Main <= TimeSpan.Zero)
            {
                p.Main = TimeSpan.Zero;
                p.IsFlagged = true;
                return true;
            }

            return false;
        }

        p.InOvertime = true;
        while (left > TimeSpan.Zero && p.Periods > 0)
        {
            if (left < p.Period)
            {
                p.Period -= left;
                return false;
            }

            left -= p.Period;
            p.Periods--;
            p.Period = Control.PeriodTime;
        }

        if (p.Periods <= 0)
        {
            p.Period = TimeSpan.Zero;
            p.IsFlagged = true;
            return true;
        }

        return false;
    }

    /// <summary><paramref name="color"/> finished a move: the period starts over, or the increment is added.</summary>
    public void MoveDone(Stone color)
    {
        PlayerClock p = For(color);
        if (p.IsFlagged)
        {
            return;
        }

        switch (Control.System)
        {
            case TimeSystem.ByoYomi when p.InOvertime:
                p.Period = Control.PeriodTime;
                break;
            case TimeSystem.Fischer:
                p.Main += Control.Increment;
                if (Control.MaxTime is { } max && p.Main > max)
                {
                    p.Main = max;
                }

                break;
        }
    }

    /// <summary>"1:05" or "1:02:05"; seconds rounded up, so 0:00 means the time is really over.</summary>
    public static string Format(TimeSpan t)
    {
        long seconds = (long)Math.Ceiling(Math.Max(0, t.TotalSeconds) - 1e-9);
        long h = seconds / 3600;
        long m = seconds / 60 % 60;
        long s = seconds % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }
}
