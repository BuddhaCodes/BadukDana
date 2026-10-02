using System.Globalization;
using System.Text.Json;
using Hoshi.Core;

namespace Hoshi.Ogs.Games;

/// <summary>
/// Reads OGS game payloads (goban <c>GobanEngineConfig</c>, <c>GameClock</c>, <c>GameChatMessage</c>,
/// verified against goban e61c56e). Moves come as packed arrays <c>[x, y, Δt?, color?, extra?]</c> (pass = -1,-1)
/// or JGOF objects <c>{x, y}</c>; stone lists as SGF letter pairs ("ddpp").
/// </summary>
public static class OgsGameParser
{
    public static OgsGameSnapshot ParseGamedata(JsonElement g)
    {
        int width = (int)(OgsJson.Long(g, "width") ?? 19);
        int height = (int)(OgsJson.Long(g, "height") ?? width);
        long blackId = OgsJson.Long(g, "black_player_id") ?? PlayerId(g, "black");
        long whiteId = OgsJson.Long(g, "white_player_id") ?? PlayerId(g, "white");
        OgsUser black = Player(g, "black", blackId);
        OgsUser white = Player(g, "white", whiteId);

        JsonElement initial = g.TryGetProperty("initial_state", out JsonElement s) ? s : default;
        var snapshot = new OgsGameSnapshot
        {
            GameId = OgsJson.Long(g, "game_id") ?? 0,
            Name = OgsJson.String(g, "game_name") ?? string.Empty,
            Width = width,
            Height = height,
            Rules = OgsJson.Rules(g, "rules") ?? RuleSet.Japanese,
            Komi = OgsJson.Double(g, "komi") ?? 0,
            Handicap = (int)(OgsJson.Long(g, "handicap") ?? 0),
            FreeHandicapPlacement = OgsJson.Bool(g, "free_handicap_placement") ?? false,
            Black = black,
            White = white,
            InitialBlack = Points(OgsJson.String(initial, "black")),
            InitialWhite = Points(OgsJson.String(initial, "white")),
            InitialPlayer = OgsJson.String(g, "initial_player") == "white" ? Stone.White : Stone.Black,
            Phase = Phase(OgsJson.String(g, "phase")),
            TimeControl = g.TryGetProperty("time_control", out JsonElement tc) ? ParseTimeControl(tc) : OgsTimeControl.None,
            Clock = g.TryGetProperty("clock", out JsonElement clock) && clock.ValueKind == JsonValueKind.Object ? ParseClock(clock) : null,
            Removed = g.TryGetProperty("removed", out JsonElement removed) ? StoneList(removed) : [],
            Ranked = OgsJson.Bool(g, "ranked") ?? false,
        };

        var moves = new List<OgsGameMove>();
        if (g.TryGetProperty("moves", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement m in list.EnumerateArray())
            {
                if (TryParseMove(m, out Point? point, out Stone explicitColor))
                {
                    int index = moves.Count;
                    // Old-style implicit passes during free handicap placement are skipped (as goban does).
                    if (point is null && snapshot.FreeHandicapPlacement && index < snapshot.Handicap)
                    {
                        continue;
                    }

                    Stone color = explicitColor != Stone.Empty ? explicitColor : snapshot.ColorForMove(index);
                    moves.Add(new OgsGameMove(index + 1, color, point));
                }
            }
        }

        return snapshot with { Moves = moves, Result = ParseResult(g, blackId, whiteId) };
    }

    public static OgsTimeControl ParseTimeControl(JsonElement tc)
    {
        if (tc.ValueKind != JsonValueKind.Object)
        {
            return OgsTimeControl.None;
        }

        string system = OgsJson.String(tc, "system") ?? OgsJson.String(tc, "time_control") ?? "none";
        return new OgsTimeControl
        {
            System = system,
            Speed = OgsJson.String(tc, "speed") ?? "live",
            MainTime = OgsJson.Double(tc, "main_time") ?? OgsJson.Double(tc, "initial_time") ?? OgsJson.Double(tc, "total_time") ?? 0,
            PeriodTime = OgsJson.Double(tc, "period_time") ?? 0,
            Periods = (int)(OgsJson.Long(tc, "periods") ?? 0),
            Increment = OgsJson.Double(tc, "time_increment") ?? 0,
            MaxTime = OgsJson.Double(tc, "max_time") ?? 0,
            PerMove = OgsJson.Double(tc, "per_move") ?? 0,
            StonesPerPeriod = (int)(OgsJson.Long(tc, "stones_per_period") ?? 0),
        };
    }

    public static OgsClock ParseClock(JsonElement c)
    {
        JsonElement pause = c.TryGetProperty("pause", out JsonElement p) ? p : default;
        return new OgsClock(
            OgsJson.Long(c, "game_id") ?? 0,
            OgsJson.Long(c, "current_player") ?? 0,
            OgsJson.Long(c, "black_player_id") ?? 0,
            OgsJson.Long(c, "white_player_id") ?? 0,
            OgsJson.Long(c, "last_move") ?? 0,
            OgsJson.Long(c, "expiration") ?? 0,
            PlayerClock(c, "black_time"),
            PlayerClock(c, "white_time"),
            OgsJson.Long(c, "paused_since") ?? OgsJson.Long(pause, "paused_since"),
            OgsJson.Bool(pause, "paused") ?? false,
            OgsJson.Bool(c, "start_mode") ?? false);
    }

    public static OgsChatLine? ParseChat(JsonElement data)
    {
        if (!data.TryGetProperty("line", out JsonElement line) || line.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string body = string.Empty;
        OgsChatKind kind = OgsChatKind.Text;
        Dictionary<string, string>? translations = null;
        if (line.TryGetProperty("body", out JsonElement b))
        {
            if (b.ValueKind == JsonValueKind.String)
            {
                body = b.GetString() ?? string.Empty;
            }
            else if (b.ValueKind == JsonValueKind.Object)
            {
                switch (OgsJson.String(b, "type"))
                {
                    case "translated":
                        kind = OgsChatKind.Translated;
                        translations = b.EnumerateObject()
                            .Where(x => x.Name != "type" && x.Value.ValueKind == JsonValueKind.String)
                            .ToDictionary(x => x.Name, x => x.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
                        body = translations.TryGetValue("en", out string? en) ? en : translations.Values.FirstOrDefault() ?? string.Empty;
                        break;
                    case "review":
                        kind = OgsChatKind.Review;
                        body = (OgsJson.Long(b, "review_id") ?? 0).ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        kind = OgsChatKind.Analysis;
                        body = OgsJson.String(b, "name") ?? string.Empty;
                        break;
                }
            }
        }

        long date = OgsJson.Long(line, "date") ?? 0;
        return new OgsChatLine(
            OgsJson.String(line, "chat_id") ?? string.Empty,
            OgsJson.String(data, "channel") ?? OgsJson.String(line, "channel") ?? "main",
            OgsJson.Long(line, "player_id") ?? 0,
            OgsJson.String(line, "username") ?? "?",
            body,
            (int)(OgsJson.Long(line, "move_number") ?? 0),
            // OGS sends seconds; tolerate milliseconds.
            date > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(date) : DateTimeOffset.FromUnixTimeSeconds(date),
            kind,
            translations);
    }

    /// <summary>The ids in <c>game/{id}/chat/remove</c> (<c>{game_id, chat_ids: [...]}</c>).</summary>
    public static IReadOnlyList<string> ParseChatRemoval(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("chat_ids", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array
            ? [.. ids.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!)]
            : [];

    /// <summary>Result from <c>winner</c> (player id or "black"/"white") + <c>outcome</c>, if the game has one.</summary>
    public static OgsGameResult? ParseResult(JsonElement e, long blackId, long whiteId)
    {
        string? outcome = OgsJson.String(e, "outcome");
        if (string.IsNullOrEmpty(outcome) || !e.TryGetProperty("winner", out JsonElement w))
        {
            return null;
        }

        Stone winner = w.ValueKind switch
        {
            JsonValueKind.String when w.GetString() == "black" => Stone.Black,
            JsonValueKind.String when w.GetString() == "white" => Stone.White,
            JsonValueKind.Number when w.GetInt64() == blackId => Stone.Black,
            JsonValueKind.Number when w.GetInt64() == whiteId => Stone.White,
            _ => Stone.Empty,
        };

        double? blackScore = null;
        double? whiteScore = null;
        if (e.TryGetProperty("score", out JsonElement score) && score.ValueKind == JsonValueKind.Object)
        {
            blackScore = score.TryGetProperty("black", out JsonElement bs) ? OgsJson.Double(bs, "total") : null;
            whiteScore = score.TryGetProperty("white", out JsonElement ws) ? OgsJson.Double(ws, "total") : null;
        }

        return new OgsGameResult(winner, outcome, blackScore, whiteScore);
    }

    /// <summary>A packed move <c>[x, y, …]</c> or <c>{x, y, color?}</c>; x = -1 is a pass.</summary>
    public static bool TryParseMove(JsonElement m, out Point? point, out Stone color)
    {
        point = null;
        color = Stone.Empty;
        long x;
        long y;
        if (m.ValueKind == JsonValueKind.Array && m.GetArrayLength() >= 2
            && m[0].ValueKind == JsonValueKind.Number && m[1].ValueKind == JsonValueKind.Number)
        {
            x = (long)m[0].GetDouble();
            y = (long)m[1].GetDouble();
            if (m.GetArrayLength() >= 4 && m[3].ValueKind == JsonValueKind.Number)
            {
                color = m[3].GetInt32() switch { 1 => Stone.Black, 2 => Stone.White, _ => Stone.Empty };
            }
        }
        else if (m.ValueKind == JsonValueKind.Object && OgsJson.Long(m, "x") is { } ox && OgsJson.Long(m, "y") is { } oy)
        {
            x = ox;
            y = oy;
            color = (int)(OgsJson.Long(m, "color") ?? 0) switch { 1 => Stone.Black, 2 => Stone.White, _ => Stone.Empty };
        }
        else
        {
            return false;
        }

        point = x < 0 || y < 0 ? null : new Point((int)x, (int)y);
        return true;
    }

    /// <summary>SGF letter pairs ("ddpp") to points; ".." (pass) and malformed pairs are skipped.</summary>
    public static IReadOnlyList<Point> Points(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        var points = new List<Point>(encoded.Length / 2);
        for (int i = 0; i + 1 < encoded.Length; i += 2)
        {
            if (Point.TryParseSgf(encoded.Substring(i, 2), out Point p))
            {
                points.Add(p);
            }
        }

        return points;
    }

    /// <summary>Points to OGS's move string ("ddpp").</summary>
    public static string Encode(IEnumerable<Point> points) => string.Concat(points.Select(p => p.ToSgf()));

    public static OgsGamePhase Phase(string? phase) => phase switch
    {
        "stone removal" => OgsGamePhase.StoneRemoval,
        "finished" => OgsGamePhase.Finished,
        _ => OgsGamePhase.Play,
    };

    private static IReadOnlyList<Point> StoneList(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => Points(e.GetString()),
        JsonValueKind.Array => [.. e.EnumerateArray()
            .Select(m => TryParseMove(m, out Point? p, out _) ? p : null)
            .OfType<Point>()],
        _ => [],
    };

    private static OgsPlayerClockState? PlayerClock(JsonElement c, string name)
    {
        if (!c.TryGetProperty(name, out JsonElement t))
        {
            return null;
        }

        return t.ValueKind switch
        {
            // Simple time: a number of milliseconds.
            JsonValueKind.Number => new OgsPlayerClockState(t.GetDouble() / 1000),
            JsonValueKind.Object => new OgsPlayerClockState(
                OgsJson.Double(t, "thinking_time") ?? 0,
                OgsJson.Long(t, "periods") is { } p ? (int)p : null,
                OgsJson.Double(t, "period_time"),
                OgsJson.Long(t, "moves_left") is { } ml ? (int)ml : null,
                OgsJson.Double(t, "block_time"),
                OgsJson.Bool(t, "skip_bonus") ?? false),
            _ => null,
        };
    }

    private static long PlayerId(JsonElement g, string color) =>
        g.TryGetProperty("players", out JsonElement players) && players.TryGetProperty(color, out JsonElement p)
            ? OgsJson.Long(p, "id") ?? 0
            : 0;

    private static OgsUser Player(JsonElement g, string color, long id)
    {
        if (g.TryGetProperty("players", out JsonElement players) && players.TryGetProperty(color, out JsonElement p)
            && p.ValueKind == JsonValueKind.Object)
        {
            OgsUser u = OgsJson.ReadUser(p);
            return u with { Id = u.Id != 0 ? u.Id : id };
        }

        return new OgsUser(id, string.Create(CultureInfo.InvariantCulture, $"#{id}"), 0, false);
    }
}
