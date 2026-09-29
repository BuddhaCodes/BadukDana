using Hoshi.Core;

namespace Hoshi.Ogs.Games;

/// <summary>
/// Remaining time from the last <see cref="OgsClock"/>, the same way the official client computes it
/// (goban <c>OGSConnectivity.computeNewPlayerClock</c>, e61c56e): only the player to move loses time, measured from
/// <c>last_move</c> in server time (local time corrected with the socket's clock drift); a paused clock stops at
/// <c>paused_since</c>; byo-yomi eats whole periods once main time is gone.
/// </summary>
public static class OgsClockMath
{
    public static (OgsClockReading Black, OgsClockReading White) Read(OgsClock clock, OgsTimeControl tc, DateTimeOffset serverNow)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tc);
        long now = serverNow.ToUnixTimeMilliseconds();
        long elapsedMs = clock.PausedSinceMs is { } paused && clock.IsPaused
            ? Math.Max(paused, clock.LastMoveMs) - clock.LastMoveMs
            : now - clock.LastMoveMs;
        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));
        Stone current = clock.StartMode ? Stone.Empty : clock.CurrentColor;
        return (
            Compute(clock.Black ?? new OgsPlayerClockState(0), current == Stone.Black, elapsed, tc),
            Compute(clock.White ?? new OgsPlayerClockState(0), current == Stone.White, elapsed, tc));
    }

    public static OgsClockReading Compute(OgsPlayerClockState state, bool isCurrent, TimeSpan elapsed, OgsTimeControl tc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(tc);
        TimeSpan thinking = TimeSpan.FromSeconds(state.ThinkingTime);
        TimeSpan running = isCurrent ? elapsed : TimeSpan.Zero;

        switch (tc.System)
        {
            case "none":
                return new OgsClockReading(TimeSpan.Zero);

            case "simple":
            {
                TimeSpan left = Max0(TimeSpan.FromSeconds(tc.PerMove) - running);
                return new OgsClockReading(left, TimedOut: left <= TimeSpan.Zero);
            }

            case "byoyomi":
            {
                TimeSpan period = TimeSpan.FromSeconds(tc.PeriodTime);
                int periods = state.Periods ?? 0;
                TimeSpan main = thinking - running;
                TimeSpan periodLeft = period;
                if (main < TimeSpan.Zero)
                {
                    TimeSpan overtime = -main;
                    main = TimeSpan.Zero;
                    if (period > TimeSpan.Zero)
                    {
                        int used = (int)(overtime.Ticks / period.Ticks);
                        periods = Math.Max(0, periods - used);
                        periodLeft = Max0(period - TimeSpan.FromTicks(overtime.Ticks - (used * period.Ticks)));
                    }
                }

                return new OgsClockReading(main, periods, periodLeft, TimedOut: main <= TimeSpan.Zero && periods == 0);
            }

            case "canadian":
            {
                TimeSpan main = thinking - running;
                TimeSpan block = TimeSpan.FromSeconds(state.BlockTime ?? 0);
                if (main < TimeSpan.Zero)
                {
                    block = Max0(block + main);
                    main = TimeSpan.Zero;
                }

                return new OgsClockReading(main, MovesLeft: state.MovesLeft, BlockLeft: block, TimedOut: main <= TimeSpan.Zero && block <= TimeSpan.Zero);
            }

            default: // fischer, absolute and anything with a single countdown
            {
                TimeSpan left = Max0(thinking - running);
                return new OgsClockReading(left, TimedOut: left <= TimeSpan.Zero && tc.System != "none");
            }
        }
    }

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
