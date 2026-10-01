using System.Globalization;
using Hoshi.Core;
using Hoshi.Core.Localization;

namespace Hoshi.Ogs.Games;

public enum OgsGamePhase
{
    Play,
    StoneRemoval,
    Finished,
}

/// <summary>JGOF time control of a game (<c>gamedata.time_control</c>). Durations in seconds.</summary>
public sealed record OgsTimeControl
{
    public static OgsTimeControl None { get; } = new() { System = "none" };

    public string System { get; init; } = "none";

    public string Speed { get; init; } = "live";

    /// <summary>Main time (byo-yomi, canadian) or initial time (fischer) or total time (absolute).</summary>
    public double MainTime { get; init; }

    public double PeriodTime { get; init; }

    public int Periods { get; init; }

    public double Increment { get; init; }

    public double MaxTime { get; init; }

    public double PerMove { get; init; }

    public int StonesPerPeriod { get; init; }

    public bool IsCorrespondence => Speed == "correspondence";
}

/// <summary>A player's clock as last sent by the server (<c>black_time</c>/<c>white_time</c>, seconds).</summary>
public sealed record OgsPlayerClockState(
    double ThinkingTime,
    int? Periods = null,
    double? PeriodTime = null,
    int? MovesLeft = null,
    double? BlockTime = null,
    bool SkipBonus = false);

/// <summary><c>game/{id}/clock</c>. Timestamps are server epoch milliseconds.</summary>
public sealed record OgsClock(
    long GameId,
    long CurrentPlayerId,
    long BlackPlayerId,
    long WhitePlayerId,
    long LastMoveMs,
    long ExpirationMs,
    OgsPlayerClockState? Black,
    OgsPlayerClockState? White,
    long? PausedSinceMs = null,
    bool IsPaused = false,
    bool StartMode = false)
{
    public Stone CurrentColor =>
        CurrentPlayerId == BlackPlayerId ? Stone.Black : CurrentPlayerId == WhitePlayerId ? Stone.White : Stone.Empty;
}

/// <summary>What a clock shows right now.</summary>
public sealed record OgsClockReading(
    TimeSpan Main,
    int? PeriodsLeft = null,
    TimeSpan? PeriodLeft = null,
    int? MovesLeft = null,
    TimeSpan? BlockLeft = null,
    bool TimedOut = false)
{
    /// <summary>Main time, or the byo-yomi period / canadian block once main time is over.</summary>
    public string Format()
    {
        if (Main > TimeSpan.Zero || (PeriodsLeft is null && BlockLeft is null))
        {
            string main = Clock(Main);
            return PeriodsLeft is { } p && PeriodLeft is { } pt
                ? string.Create(CultureInfo.InvariantCulture, $"{main} + {p}×{Clock(pt)}")
                : main;
        }

        if (PeriodsLeft is { } periods && PeriodLeft is { } left)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Clock(left)} ({periods})");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Clock(BlockLeft ?? TimeSpan.Zero)} /{MovesLeft ?? 0}");
    }

    internal static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        // Round up like a countdown: 0.2 s left still shows 0:01.
        long seconds = (long)Math.Ceiling(t.TotalSeconds - 1e-9);
        if (seconds >= 86400)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds / 86400} d {seconds % 86400 / 3600} h");
        }

        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}

/// <summary>One move of an online game. <see cref="Number"/> is 1-based; a null point is a pass.</summary>
public sealed record OgsGameMove(int Number, Stone Color, Point? Point);

/// <summary>How a game ended.</summary>
public sealed record OgsGameResult(Stone Winner, string Outcome, double? BlackScore = null, double? WhiteScore = null)
{
    /// <summary>SGF <c>RE</c> value: B+R, W+T, B+3.5, Void…</summary>
    public string ToSgf()
    {
        if (Winner == Stone.Empty)
        {
            return Outcome.Contains("tie", StringComparison.OrdinalIgnoreCase) ? "0" : "Void";
        }

        string w = Winner == Stone.Black ? "B" : "W";
        string o = Outcome.Trim();
        if (o.StartsWith("Resignation", StringComparison.OrdinalIgnoreCase))
        {
            return w + "+R";
        }

        if (o.StartsWith("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return w + "+T";
        }

        string number = new(o.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return number.Length > 0 ? $"{w}+{number}" : w + "+F";
    }

    /// <summary>Localized summary for the UI.</summary>
    public string Describe()
    {
        if (Winner == Stone.Empty)
        {
            return Tr.T("Ogs.Result.Annulled");
        }

        string who = Tr.T(Winner == Stone.Black ? "Ogs.Result.BlackWins" : "Ogs.Result.WhiteWins");
        string sgf = ToSgf();
        return sgf[2..] switch
        {
            "R" => Tr.F("Ogs.Result.ByResignation", who),
            "T" => Tr.F("Ogs.Result.ByTime", who),
            "F" => $"{who} ({Outcome})",
            var points => Tr.F("Ogs.Result.ByPoints", who, points),
        };
    }
}

public sealed record OgsChatLine(string ChatId, string Channel, long PlayerId, string Username, string Body, int MoveNumber, DateTimeOffset Date);

/// <summary>Full state of an online game (<c>game/{id}/gamedata</c>).</summary>
public sealed record OgsGameSnapshot
{
    public required long GameId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Width { get; init; } = 19;

    public int Height { get; init; } = 19;

    public RuleSet Rules { get; init; } = RuleSet.Japanese;

    public double Komi { get; init; }

    public int Handicap { get; init; }

    public bool FreeHandicapPlacement { get; init; }

    public required OgsUser Black { get; init; }

    public required OgsUser White { get; init; }

    public IReadOnlyList<Point> InitialBlack { get; init; } = [];

    public IReadOnlyList<Point> InitialWhite { get; init; } = [];

    public Stone InitialPlayer { get; init; } = Stone.Black;

    public IReadOnlyList<OgsGameMove> Moves { get; init; } = [];

    public OgsGamePhase Phase { get; init; } = OgsGamePhase.Play;

    public OgsTimeControl TimeControl { get; init; } = OgsTimeControl.None;

    public OgsClock? Clock { get; init; }

    /// <summary>Stones marked dead / dame during stone removal.</summary>
    public IReadOnlyList<Point> Removed { get; init; } = [];

    public OgsGameResult? Result { get; init; }

    public bool Ranked { get; init; }

    public Stone ColorOf(long playerId) =>
        playerId == Black.Id ? Stone.Black : playerId == White.Id ? Stone.White : Stone.Empty;

    /// <summary>
    /// Colour of the move at 0-based <paramref name="index"/>: with free handicap placement black plays the first
    /// <c>handicap</c> moves; after that colours alternate (as goban's engine does).
    /// </summary>
    public Stone ColorForMove(int index)
    {
        if (FreeHandicapPlacement && Handicap > 1)
        {
            return index < Handicap ? Stone.Black : ((index - Handicap) % 2 == 0 ? Stone.White : Stone.Black);
        }

        return index % 2 == 0 ? InitialPlayer : InitialPlayer.Opponent();
    }
}
