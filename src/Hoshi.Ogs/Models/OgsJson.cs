using System.Globalization;
using System.Text.Json;
using Hoshi.Core;
using Hoshi.Core.Localization;

namespace Hoshi.Ogs;

/// <summary>
/// Tolerant readers for OGS JSON. OGS payloads vary between endpoints (e.g. <c>pro</c> vs <c>professional</c>,
/// numbers sent as strings), so fields are read defensively instead of through fixed DTOs.
/// </summary>
internal static class OgsJson
{
    public static OgsUser ReadUser(JsonElement e) => new(
        Long(e, "id") ?? 0,
        String(e, "username") ?? "?",
        Double(e, "ranking") ?? Double(e, "rank") ?? 0,
        Bool(e, "professional") ?? Bool(e, "pro") ?? false);

    public static string? String(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    public static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out long l) => l,
            JsonValueKind.Number => (long)v.GetDouble(),
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) => s,
            JsonValueKind.Object => Long(v, "id"),
            _ => null,
        };
    }

    public static double? Double(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => null,
        };
    }

    public static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => v.GetDouble() != 0,
                _ => null,
            }
            : null;

    public static RuleSet? Rules(JsonElement e, string name) =>
        String(e, name) is { } r && RuleSet.TryFromName(r, out RuleSet? rules) ? rules : null;

    /// <summary>Short human summary of a JGOF time control, e.g. "byoyomi 10:00 + 5×30 s".</summary>
    public static string TimeSummary(JsonElement tc)
    {
        string system = String(tc, "system") ?? String(tc, "time_control") ?? "?";
        return system switch
        {
            "byoyomi" => Tr.F("Ogs.Time.Byoyomi", Clock(Double(tc, "main_time")), Long(tc, "periods") ?? 0, Seconds(Double(tc, "period_time"))),
            "fischer" => Tr.F("Ogs.Time.Fischer", Clock(Double(tc, "initial_time")), Seconds(Double(tc, "time_increment")), Clock(Double(tc, "max_time"))),
            "canadian" => Tr.F("Ogs.Time.Canadian", Clock(Double(tc, "main_time")), Clock(Double(tc, "period_time")), Long(tc, "stones_per_period") ?? 0),
            "simple" => Tr.F("Ogs.Time.Simple", Seconds(Double(tc, "per_move"))),
            "absolute" => Tr.F("Ogs.Time.Absolute", Clock(Double(tc, "total_time"))),
            "none" => Tr.T("Ogs.Time.None"),
            _ => system,
        };
    }

    private static string Clock(double? seconds)
    {
        var t = TimeSpan.FromSeconds(seconds ?? 0);
        return t.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{t.TotalDays:0.#} d")
            : t.TotalHours >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
    }

    private static string Seconds(double? seconds)
    {
        double s = seconds ?? 0;
        return s >= 86400
            ? string.Create(CultureInfo.InvariantCulture, $"{s / 86400:0.#} d")
            : s >= 3600
                ? string.Create(CultureInfo.InvariantCulture, $"{s / 3600:0.#} h")
                : string.Create(CultureInfo.InvariantCulture, $"{s:0} s");
    }
}
