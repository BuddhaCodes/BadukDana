using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hoshi.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Ogs.Joseki;

/// <summary>How the OGS Joseki Explorer rates a move (backend <c>PlayCategory</c>).</summary>
public enum JosekiCategory
{
    Ideal,
    Good,
    Mistake,
    Trick,
    Question,
    Unknown,
}

/// <summary>A move the explorer knows from a position; a null point is tenuki ("pass" in the explorer).</summary>
public sealed record JosekiNextMove(string NodeId, Point? Point, JosekiCategory Category, string? Label)
{
    public bool IsTenuki => Point is null;

    /// <summary>Ideal or good: a move to follow (mistakes, tricks and open questions are only shown).</summary>
    public bool IsRecommended => Category is JosekiCategory.Ideal or JosekiCategory.Good;
}

/// <summary>A node of the explorer's tree. Points are in the explorer's own orientation (see <see cref="JosekiExplorer.CornerAsync"/>).</summary>
public sealed record JosekiPosition(
    string NodeId,
    Point? Placement,
    bool IsRoot,
    JosekiCategory Category,
    string? Description,
    IReadOnlyList<JosekiNextMove> NextMoves,
    IReadOnlyList<string> Tags);

public interface IJosekiExplorerSource
{
    Task<JosekiPosition> GetPositionAsync(string nodeId, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only client of the OGS Joseki Explorer ("OJE"): <c>GET /oje/position?id=&lt;node&gt;&amp;mode=0</c> with no
/// authentication, as the web client's explore mode calls it (online-go.com <c>src/views/Joseki/joseki-utils.ts</c>
/// and <c>Joseki.tsx</c>, read 2026-10-01). Positions are cached in memory and, when a folder is given, on disk
/// for a week, so a corner is fetched once and reviewing is instant and gentle on the server.
/// </summary>
public sealed partial class OgsJosekiClient : IJosekiExplorerSource
{
    public static readonly Uri DefaultBaseUrl = new("https://online-go.com/oje/");

    private static readonly TimeSpan DiskLifetime = TimeSpan.FromDays(7);
    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly string? _cacheDirectory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, JosekiPosition> _memory = new(StringComparer.Ordinal);

    public OgsJosekiClient(HttpClient http, Uri? baseUrl = null, string? cacheDirectory = null, ILogger<OgsJosekiClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = baseUrl ?? DefaultBaseUrl;
        _cacheDirectory = cacheDirectory;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public async Task<JosekiPosition> GetPositionAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        if (_memory.TryGetValue(nodeId, out JosekiPosition? known))
        {
            return known;
        }

        string? file = _cacheDirectory is null || !SafeId().IsMatch(nodeId) ? null : Path.Combine(_cacheDirectory, nodeId + ".json");
        if (file is not null && File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < DiskLifetime)
        {
            try
            {
                return _memory[nodeId] = Parse(await File.ReadAllTextAsync(file, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException)
            {
                _logger.LogDebug(ex, "Ignoring cached joseki position {Node}", nodeId);
            }
        }

        var uri = new Uri(_baseUrl, "position?id=" + Uri.EscapeDataString(nodeId) + "&mode=0");
        using HttpResponseMessage response = await _http.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"Joseki Explorer answered {(int)response.StatusCode} for position {nodeId}."), null, response.StatusCode);
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        JosekiPosition position = Parse(json);
        _memory[nodeId] = position;
        if (position.IsRoot)
        {
            _memory.TryAdd("root", position);
        }

        if (file is not null)
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory!);
                await File.WriteAllTextAsync(file, json, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not cache joseki position {Node}", nodeId);
            }
        }

        return position;
    }

    /// <summary>Reads an explorer position; unknown fields are ignored and odd ones tolerated.</summary>
    public static JosekiPosition Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A joseki position must be a JSON object.");
        }

        string placement = Text(root, "placement") ?? string.Empty;
        bool isRoot = placement.Equals("root", StringComparison.OrdinalIgnoreCase);
        var next = new List<JosekiNextMove>();
        if (root.TryGetProperty("next_moves", out JsonElement moves) && moves.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement m in moves.EnumerateArray())
            {
                string? p = Text(m, "placement");
                string? id = Text(m, "node_id");
                if (p is null || id is null)
                {
                    continue;
                }

                bool tenuki = p.Equals("pass", StringComparison.OrdinalIgnoreCase);
                if (!tenuki && !Point.TryParseHuman(p, 19, out _))
                {
                    continue;
                }

                next.Add(new JosekiNextMove(id, tenuki ? null : Point.FromHuman(p, 19), Category(Text(m, "category")), Text(m, "variation_label")));
            }
        }

        var tags = new List<string>();
        if (root.TryGetProperty("tags", out JsonElement t) && t.ValueKind == JsonValueKind.Array)
        {
            tags.AddRange(t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object ? Text(x, "description") : null).OfType<string>());
        }

        Point? at = !isRoot && Point.TryParseHuman(placement, 19, out Point point) ? point : null;
        return new JosekiPosition(
            Text(root, "node_id") ?? (isRoot ? "root" : throw new FormatException("A joseki position needs a node_id.")),
            at,
            isRoot,
            Category(Text(root, "category")),
            Clean(Text(root, "description")),
            next,
            tags);
    }

    /// <summary>Explorer markdown marks (<c>&lt;A:Q16&gt;</c>, <c>&lt;position: 12&gt;</c>) as plain text.</summary>
    internal static string? Clean(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        string s = Marks().Replace(description, "$1");
        s = PositionLinks().Replace(s, "#$1");
        return s.Trim();
    }

    private static JosekiCategory Category(string? value) => value?.ToUpperInvariant() switch
    {
        "IDEAL" => JosekiCategory.Ideal,
        "GOOD" => JosekiCategory.Good,
        "MISTAKE" => JosekiCategory.Mistake,
        "TRICK" => JosekiCategory.Trick,
        "QUESTION" => JosekiCategory.Question,
        _ => JosekiCategory.Unknown,
    };

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex SafeId();

    [GeneratedRegex("<([A-Z]):[A-Z][0-9]{1,2}>")]
    private static partial Regex Marks();

    [GeneratedRegex("<position: *([0-9]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex PositionLinks();
}

/// <summary>Walks the explorer's tree along a corner sequence.</summary>
public sealed class JosekiExplorer(IJosekiExplorerSource source)
{
    private Corner? _corner;

    public IJosekiExplorerSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>The position after <paramref name="moves"/> (explorer orientation; null = tenuki), or null when the sequence leaves the explorer's tree.</summary>
    public async Task<JosekiPosition?> FollowAsync(IReadOnlyList<Point?> moves, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moves);
        JosekiPosition position = await Source.GetPositionAsync("root", cancellationToken);
        foreach (Point? move in moves)
        {
            JosekiNextMove? next = position.NextMoves.FirstOrDefault(m => m.Point == move);
            if (next is null)
            {
                return null;
            }

            position = await Source.GetPositionAsync(next.NodeId, cancellationToken);
        }

        return position;
    }

    /// <summary>The corner the explorer's moves are in (its first moves decide; top-right on OGS today).</summary>
    public async Task<Corner> CornerAsync(CancellationToken cancellationToken)
    {
        if (_corner is { } known)
        {
            return known;
        }

        JosekiPosition root = await Source.GetPositionAsync("root", cancellationToken);
        Corner corner = root.NextMoves.Where(m => m.Point is not null)
            .GroupBy(m => Corners.Nearest(m.Point!.Value, 19))
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .DefaultIfEmpty(Corner.TopRight)
            .First();
        _corner = corner;
        return corner;
    }
}
