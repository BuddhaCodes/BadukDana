using System.Globalization;
using Hoshi.Core;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Gtp;

/// <summary>The analysis command a GTP engine understands.</summary>
public enum GtpAnalysisKind
{
    /// <summary>The engine has no analysis command.</summary>
    None,

    /// <summary><c>lz-analyze</c> (Leela Zero, SAI, KataGo): winrate and prior as integers 0–10000.</summary>
    Leela,

    /// <summary><c>kata-analyze</c> (KataGo): winrate in [0,1], score lead and ownership.</summary>
    KataGo,
}

/// <summary>
/// Parses the <c>info</c> lines of <c>lz-analyze</c> / <c>kata-analyze</c> (KataGo docs/GTP_Extensions.md):
/// <c>info move D4 visits 120 winrate 5312 prior 812 lcb 5100 order 0 pv D4 Q16 … info move …</c>, with KataGo's
/// <c>scoreLead</c>, an optional <c>rootInfo …</c> and <c>ownership</c> (height×width values from the top-left).
/// Engines report from the side to move; the result is converted to Black's point of view like the JSON analysis.
/// </summary>
public static class GtpAnalysisParser
{
    private static readonly HashSet<string> Sections = ["info", "rootInfo", "ownership", "ownershipStdev", "movesOwnership", "movesOwnershipStdev"];

    public static TurnAnalysis? Parse(string line, GtpAnalysisKind kind, int width, int height, Stone toMove, int turn)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double sign = toMove == Stone.White ? -1 : 1;
        var candidates = new List<MoveCandidate>();
        Dictionary<string, string>? root = null;
        double[]? ownership = null;
        int i = 0;
        while (i < t.Length)
        {
            switch (t[i])
            {
                case "info":
                {
                    i++;
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    var pv = new List<Point?>();
                    while (i < t.Length && !Sections.Contains(t[i]))
                    {
                        if (t[i] == "pv")
                        {
                            i++;
                            while (i < t.Length && IsVertex(t[i], height))
                            {
                                pv.Add(Vertex(t[i], height));
                                i++;
                            }

                            continue;
                        }

                        if (t[i] is "pvVisits" or "pvEdgeVisits")
                        {
                            // One number per pv move.
                            i++;
                            while (i < t.Length && double.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            {
                                i++;
                            }

                            continue;
                        }

                        if (i + 1 < t.Length && !Sections.Contains(t[i + 1]))
                        {
                            values[t[i]] = t[i + 1];
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (values.TryGetValue("move", out string? move) && IsVertex(move, height))
                    {
                        candidates.Add(Candidate(values, Vertex(move, height), candidates.Count, pv, kind, sign));
                    }

                    break;
                }

                case "rootInfo":
                {
                    i++;
                    root = new Dictionary<string, string>(StringComparer.Ordinal);
                    while (i + 1 < t.Length && !Sections.Contains(t[i]))
                    {
                        root[t[i]] = t[i + 1];
                        i += 2;
                    }

                    break;
                }

                case "ownership":
                {
                    i++;
                    int n = width * height;
                    if (i + n <= t.Length)
                    {
                        var own = new double[n];
                        bool ok = true;
                        for (int k = 0; k < n && ok; k++)
                        {
                            ok = double.TryParse(t[i + k], NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                            own[k] = v * sign;
                        }

                        ownership = ok ? own : null;
                        i += n;
                    }
                    else
                    {
                        i = t.Length;
                    }

                    break;
                }

                default:
                    i++;
                    break;
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((a, b) => a.Order.CompareTo(b.Order));
        MoveCandidate best = candidates[0];
        double winrate = best.Winrate;
        double score = best.ScoreLead;
        int visits = candidates.Sum(c => c.Visits);
        if (root is not null)
        {
            if (Number(root, "winrate") is { } w)
            {
                winrate = Black(Rate(w, kind), sign);
            }

            if (Number(root, "scoreLead") is { } s)
            {
                score = s * sign;
            }

            if (Number(root, "visits") is { } v)
            {
                visits = (int)v;
            }
        }

        return new TurnAnalysis(turn, toMove, winrate, score, visits, candidates, ownership);
    }

    private static MoveCandidate Candidate(Dictionary<string, string> v, Point? point, int index, List<Point?> pv, GtpAnalysisKind kind, double sign)
    {
        double winrate = Number(v, "winrate") is { } w ? Rate(w, kind) : 0.5;
        double prior = Number(v, "prior") is { } p ? Rate(p, kind) : 0;
        double score = Number(v, "scoreLead") ?? Number(v, "scoreMean") ?? 0;
        return new MoveCandidate(
            point,
            Number(v, "order") is { } o ? (int)o : index,
            Number(v, "visits") is { } n ? (int)n : 0,
            Black(winrate, sign),
            score * sign,
            prior,
            pv);
    }

    /// <summary>lz-analyze reports rates as 0–10000; kata-analyze as 0–1.</summary>
    private static double Rate(double value, GtpAnalysisKind kind) => kind == GtpAnalysisKind.Leela ? value / 10000.0 : value;

    private static double Black(double winrate, double sign) => sign > 0 ? winrate : 1 - winrate;

    private static double? Number(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

    private static bool IsVertex(string s, int height) =>
        s.Equals("pass", StringComparison.OrdinalIgnoreCase) || Point.TryParseHuman(s, height, out _);

    private static Point? Vertex(string s, int height) =>
        Point.TryParseHuman(s, height, out Point p) ? p : null;
}
