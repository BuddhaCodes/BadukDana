using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Hoshi.Core;

namespace Hoshi.Ogs;

/// <summary>An OGS account. <see cref="Ranking"/> is OGS's continuous rank (0 = 30k, 30 = 1d).</summary>
public sealed record OgsUser(long Id, string Username, double Ranking, bool Professional)
{
    public string Rank => OgsRank.Format(Ranking, Professional);

    public string DisplayName => $"{Username} [{Rank}]";
}

/// <summary>A signed-in session: who we are and the JWT the realtime socket authenticates with.</summary>
public sealed record OgsSession(OgsUser User, string UserJwt);

public static class OgsRank
{
    /// <summary>Same rules as the web client's <c>rankString</c> (without tenths or provisional marks).</summary>
    public static string Format(double ranking, bool professional)
    {
        if (professional || ranking > 900)
        {
            double pro = ranking > 900 ? ranking - 1000 - 36 : ranking - 36;
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(pro))}p");
        }

        return ranking < 30
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Ceiling(30 - ranking)}k")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Floor(ranking - 29)}d");
    }
}

/// <summary>One of the signed-in user's ongoing games (from <c>ui/overview</c>).</summary>
public sealed record OgsActiveGame(long Id, string Name, OgsUser Black, OgsUser White, int Width, int Height, long? PlayerToMove, string Phase)
{
    public bool IsTurnOf(long userId) => Phase == "play" && PlayerToMove == userId;

    public OgsUser OpponentOf(long userId) => Black.Id == userId ? White : Black;
}

/// <summary>An open challenge from the seek graph.</summary>
public sealed record OgsOpenChallenge(
    long ChallengeId,
    long GameId,
    OgsUser Challenger,
    string Name,
    int Width,
    int Height,
    bool Ranked,
    int Handicap,
    double? Komi,
    RuleSet? Rules,
    string ChallengerColor,
    string Speed,
    string TimeControlSummary,
    double MinRank,
    double MaxRank);

public enum ChallengeColor
{
    Automatic,
    Black,
    White,
    Random,
}

/// <summary>
/// A new challenge. Always unranked: Hoshi is a development build and must not create ranked games
/// (see CLAUDE.md). The JSON sent mirrors the official web client's ChallengeModal.
/// </summary>
public sealed record ChallengeRequest
{
    public string Name { get; init; } = "Hoshi";

    public int Width { get; init; } = 19;

    public int Height { get; init; } = 19;

    public RuleSet Rules { get; init; } = RuleSet.Japanese;

    public int Handicap { get; init; }

    /// <summary>Null for automatic komi.</summary>
    public double? Komi { get; init; }

    public ChallengeColor Color { get; init; } = ChallengeColor.Automatic;

    public bool Private { get; init; }

    public bool InviteOnly { get; init; }

    public bool DisableAnalysis { get; init; }

    public double MinRanking { get; init; } = -1000;

    public double MaxRanking { get; init; } = 1000;

    public TimeControlSettings TimeControl { get; init; } =
        TimeControlSettings.ByoYomi(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), 5);

    public JsonObject ToJson() => new()
    {
        ["initialized"] = false,
        ["challenger_color"] = Color.ToString().ToLowerInvariant(),
        ["invite_only"] = InviteOnly,
        ["min_ranking"] = MinRanking,
        ["max_ranking"] = MaxRanking,
        ["rengo_auto_start"] = 0,
        ["game"] = GameJson(),
    };

    private JsonObject GameJson()
    {
        var game = new JsonObject
        {
            ["name"] = Name,
            ["rules"] = Rules.Name,
            ["ranked"] = false,
            ["width"] = Width,
            ["height"] = Height,
            ["handicap"] = Handicap,
        };
        if (Komi is { } k)
        {
            game["komi"] = k;
            game["komi_auto"] = "custom";
        }
        else
        {
            game["komi_auto"] = "automatic";
        }

        game["disable_analysis"] = DisableAnalysis;
        game["initial_state"] = null;
        game["private"] = Private;
        game["time_control"] = TimeControl.System;
        game["time_control_parameters"] = TimeControl.ToJson(Width, Height);
        game["pause_on_weekends"] = false;
        return game;
    }
}

public sealed record CreatedChallenge(long ChallengeId, long GameId, bool IsLive);

/// <summary>A time control in OGS's JGOF format. Durations are whole seconds on the wire.</summary>
public sealed record TimeControlSettings
{
    private readonly (string Key, double Seconds)[] _fields;

    private TimeControlSettings(string system, params (string Key, double Seconds)[] fields)
    {
        System = system;
        _fields = fields;
    }

    public string System { get; }

    /// <summary>OGS speed class for a 19×19 board ("blitz", "live" or "correspondence").</summary>
    public string Speed => SpeedFor(19, 19);

    public static TimeControlSettings ByoYomi(TimeSpan mainTime, TimeSpan periodTime, int periods) =>
        new("byoyomi", ("main_time", mainTime.TotalSeconds), ("period_time", periodTime.TotalSeconds), ("periods", periods));

    public static TimeControlSettings Fischer(TimeSpan initialTime, TimeSpan increment, TimeSpan maxTime) =>
        new("fischer", ("initial_time", initialTime.TotalSeconds), ("time_increment", increment.TotalSeconds), ("max_time", maxTime.TotalSeconds));

    public static TimeControlSettings Simple(TimeSpan perMove) => new("simple", ("per_move", perMove.TotalSeconds));

    /// <summary>
    /// Speed as classified by the official client: average seconds per move (goban's computeAverageMoveTime,
    /// assuming 0.7·w·h/2 moves per player) — 0 or above 3600 is correspondence, below 10 is blitz, else live.
    /// </summary>
    public string SpeedFor(int width, int height)
    {
        double moves = Math.Round(0.7 * width * height) / 2;
        double t = System switch
        {
            "fischer" => (Get("initial_time") / moves) + Get("time_increment"),
            "byoyomi" => (Get("main_time") / moves) + Get("period_time"),
            "simple" => Get("per_move"),
            _ => 0,
        };
        double tpm = Math.Round(t, MidpointRounding.AwayFromZero);
        return tpm == 0 || tpm > 3600 ? "correspondence" : tpm < 10 ? "blitz" : "live";
    }

    public JsonObject ToJson(int width = 19, int height = 19)
    {
        var json = new JsonObject { ["system"] = System, ["speed"] = SpeedFor(width, height) };
        foreach ((string key, double seconds) in _fields)
        {
            json[key] = (long)Math.Round(seconds);
        }

        json["pause_on_weekends"] = false;
        json["time_control"] = System; // legacy duplicate the backend still expects
        return json;
    }

    private double Get(string key) => _fields.FirstOrDefault(f => f.Key == key).Seconds;
}

public sealed class OgsApiException : Exception
{
    public OgsApiException()
    {
    }

    public OgsApiException(string message)
        : base(message)
    {
    }

    public OgsApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OgsApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
