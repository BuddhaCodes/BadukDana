using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Core;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Reviews;

/// <summary>
/// One entry of a review's log (goban's <c>ReviewMessage</c>, <c>src/engine/GobanEngine.ts</c>): where the
/// controller is (<see cref="From"/> = the move of the game where the shown line leaves it, <see cref="Moves"/> =
/// the line from there), the node's text, its marks, and who owns or controls the room.
/// </summary>
public sealed record OgsReviewMessage
{
    public int? From { get; init; }

    /// <summary>The line after <see cref="From"/>; a null point is a pass. Null when the entry does not move.</summary>
    public IReadOnlyList<Point?>? Moves { get; init; }

    public string? Text { get; init; }

    public string? TextAppend { get; init; }

    /// <summary>Marks by kind ("circle", "square", "triangle", "cross", or a letter) → points.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Point>>? Marks { get; init; }

    public long? OwnerId { get; init; }

    public long? ControllerId { get; init; }

    /// <summary>The reviewed game (from the log's first entry, its <c>gamedata</c>).</summary>
    public long? GameId { get; init; }

    public string? ChatBody { get; init; }

    public long? ChatPlayerId { get; init; }
}

/// <summary>
/// A review room on OGS (teacher mode): <c>review/connect</c> and <c>review/disconnect</c>, the log replayed with
/// <c>review/{id}/full_state</c> and grown with <c>review/{id}/r</c>, and <c>review/append</c> to show a position
/// to everyone in the room (only the controller's appends are taken). Checked against goban's
/// <c>ClientToServer.ts</c>, <c>ServerToClient.ts</c> and <c>OGSConnectivity.ts</c> (2026-11-02). Reconnects
/// with the socket. Events are raised on the socket thread.
/// </summary>
public sealed class OgsReviewSession : IDisposable
{
    private readonly OgsRealtimeClient _client;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;

    public OgsReviewSession(OgsRealtimeClient client, long reviewId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ReviewId = reviewId;
    }

    public long ReviewId { get; }

    /// <summary>The whole log (first connection, every reconnection, and after a save).</summary>
    public event EventHandler<IReadOnlyList<OgsReviewMessage>>? FullState;

    /// <summary>One new entry.</summary>
    public event EventHandler<OgsReviewMessage>? Appended;

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _subscriptions.Add(_client.Subscribe($"review/{ReviewId}/full_state", d =>
            FullState?.Invoke(this, d.ValueKind == JsonValueKind.Array ? [.. d.EnumerateArray().Select(Parse)] : [])));
        _subscriptions.Add(_client.Subscribe($"review/{ReviewId}/r", d => Appended?.Invoke(this, Parse(d))));
        _client.Connected += OnReconnected;
        SendConnect();
    }

    /// <summary>Shows a position to the room: the line from move <paramref name="from"/> of the game, its text and marks.</summary>
    public void Show(int from, IReadOnlyList<Point?> moves, string text, IReadOnlyDictionary<string, IReadOnlyList<Point>> marks)
    {
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(marks);
        var k = new JsonObject();
        foreach ((string kind, IReadOnlyList<Point> points) in marks)
        {
            k[kind] = string.Concat(points.Select(p => p.ToSgf()));
        }

        _client.Send("review/append", new JsonObject
        {
            ["review_id"] = ReviewId,
            ["f"] = from,
            ["m"] = Encode(moves),
            ["t"] = text,
            ["k"] = k,
        });
    }

    /// <summary>A chat line in the room, said at the position shown.</summary>
    public void Chat(int from, IReadOnlyList<Point?> moves, string body) =>
        _client.Send("review/chat", new JsonObject { ["review_id"] = ReviewId, ["from"] = from, ["moves"] = Encode(moves), ["body"] = body });

    /// <summary>Gives the control of the room to another player (only the owner can).</summary>
    public void PassControl(long playerId) =>
        _client.Send("review/append", new JsonObject { ["review_id"] = ReviewId, ["controller"] = playerId });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Connected -= OnReconnected;
        foreach (IDisposable s in _subscriptions)
        {
            s.Dispose();
        }

        try
        {
            _client.Send("review/disconnect", new JsonObject { ["review_id"] = ReviewId });
        }
        catch (Exception ex) when (ex is InvalidOperationException or OgsRealtimeException)
        {
            // the socket is already gone
        }
    }

    /// <summary>goban's packed moves: two SGF letters per move, ".." for a pass.</summary>
    public static string Encode(IReadOnlyList<Point?> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var sb = new StringBuilder(moves.Count * 2);
        foreach (Point? m in moves)
        {
            sb.Append(m is { } p ? p.ToSgf() : "..");
        }

        return sb.ToString();
    }

    /// <summary>Reads packed moves; edited stones ("!" + colour + point) are not moves and are skipped.</summary>
    public static IReadOnlyList<Point?> Decode(string packed)
    {
        var moves = new List<Point?>();
        int i = 0;
        while (i + 1 < packed.Length)
        {
            if (packed[i] == '!')
            {
                i += 4; // "!1dd": an edited stone
                continue;
            }

            string pair = packed.Substring(i, 2);
            if (pair == "..")
            {
                moves.Add(null);
            }
            else if (Point.TryParseSgf(pair, out Point p))
            {
                moves.Add(p);
            }

            i += 2;
        }

        return moves;
    }

    public static OgsReviewMessage Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return new OgsReviewMessage();
        }

        IReadOnlyDictionary<string, IReadOnlyList<Point>>? marks = null;
        if (e.TryGetProperty("k", out JsonElement k) && k.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, IReadOnlyList<Point>>();
            foreach (JsonProperty mark in k.EnumerateObject())
            {
                if (mark.Value.ValueKind == JsonValueKind.String)
                {
                    dict[mark.Name] = [.. Decode(mark.Value.GetString() ?? string.Empty).Where(p => p is not null).Select(p => p!.Value)];
                }
            }

            marks = dict;
        }

        return new OgsReviewMessage
        {
            From = e.TryGetProperty("f", out JsonElement f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : null,
            Moves = e.TryGetProperty("m", out JsonElement m) && m.ValueKind == JsonValueKind.String ? Decode(m.GetString() ?? string.Empty) : null,
            Text = e.TryGetProperty("t", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            TextAppend = e.TryGetProperty("t+", out JsonElement tp) && tp.ValueKind == JsonValueKind.String ? tp.GetString() : null,
            Marks = marks,
            OwnerId = Id(e, "owner"),
            ControllerId = Id(e, "controller"),
            GameId = e.TryGetProperty("gamedata", out JsonElement g) && g.ValueKind == JsonValueKind.Object ? OgsJson.Long(g, "game_id") : null,
            ChatBody = e.TryGetProperty("chat", out JsonElement c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty("body", out JsonElement b)
                ? b.ValueKind == JsonValueKind.String ? b.GetString() : b.ToString()
                : null,
            ChatPlayerId = e.TryGetProperty("chat", out JsonElement c2) && c2.ValueKind == JsonValueKind.Object ? OgsJson.Long(c2, "player_id") : null,
        };
    }

    // "owner" and "controller" are a player id or {id, username}.
    private static long? Id(JsonElement e, string name) =>
        !e.TryGetProperty(name, out JsonElement v) ? null
        : v.ValueKind == JsonValueKind.Number ? v.GetInt64()
        : v.ValueKind == JsonValueKind.Object ? OgsJson.Long(v, "id")
        : null;

    private void OnReconnected(object? sender, EventArgs e) => SendConnect();

    private void SendConnect() => _client.Send("review/connect", new JsonObject { ["review_id"] = ReviewId });
}
