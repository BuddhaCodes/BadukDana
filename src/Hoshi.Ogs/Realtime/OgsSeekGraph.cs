using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hoshi.Ogs.Realtime;

/// <summary>
/// Live list of open challenges (<c>seek_graph/connect</c> → <c>seekgraph/global</c>). Re-subscribes after every
/// reconnection. Messages are arrays of challenges, <c>{challenge_id, delete: true}</c> or
/// <c>{challenge_id, game_started: true}</c>.
/// </summary>
public sealed class OgsSeekGraph : IDisposable
{
    private readonly OgsRealtimeClient _client;
    private readonly IDisposable _subscription;
    private readonly Dictionary<long, OgsOpenChallenge> _challenges = [];
    private readonly object _gate = new();
    private bool _disposed;

    public OgsSeekGraph(OgsRealtimeClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _subscription = client.Subscribe("seekgraph/global", OnMessages);
        client.Connected += OnConnected;
        client.Disconnected += OnDisconnected;
        if (client.State == OgsConnectionState.Connected)
        {
            SendConnect();
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<OgsOpenChallenge> Challenges
    {
        get
        {
            lock (_gate)
            {
                return [.. _challenges.Values.OrderBy(c => c.ChallengeId)];
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Connected -= OnConnected;
        _client.Disconnected -= OnDisconnected;
        _subscription.Dispose();
        _client.Send("seek_graph/disconnect", new JsonObject { ["channel"] = "global" });
    }

    internal static OgsOpenChallenge? Parse(JsonElement e)
    {
        if (OgsJson.Long(e, "challenge_id") is not { } id || OgsJson.String(e, "username") is null)
        {
            return null;
        }

        JsonElement tc = e.TryGetProperty("time_control_parameters", out JsonElement t) ? t : default;
        return new OgsOpenChallenge(
            id,
            OgsJson.Long(e, "game_id") ?? 0,
            new OgsUser(OgsJson.Long(e, "user_id") ?? 0, OgsJson.String(e, "username")!, OgsJson.Double(e, "ranking") ?? 0, OgsJson.Bool(e, "professional") ?? false),
            OgsJson.String(e, "name") ?? string.Empty,
            (int)(OgsJson.Long(e, "width") ?? 19),
            (int)(OgsJson.Long(e, "height") ?? 19),
            OgsJson.Bool(e, "ranked") ?? false,
            (int)(OgsJson.Long(e, "handicap") ?? 0),
            OgsJson.String(e, "komi_auto") == "custom" ? OgsJson.Double(e, "komi") : null,
            OgsJson.Rules(e, "rules"),
            OgsJson.String(e, "challenger_color") ?? "automatic",
            tc.ValueKind == JsonValueKind.Object ? OgsJson.String(tc, "speed") ?? "live" : "live",
            tc.ValueKind == JsonValueKind.Object ? OgsJson.TimeSummary(tc) : OgsJson.String(e, "time_control") ?? "?",
            OgsJson.Double(e, "min_rank") ?? -1000,
            OgsJson.Double(e, "max_rank") ?? 1000);
    }

    private void SendConnect() => _client.Send("seek_graph/connect", new JsonObject { ["channel"] = "global" });

    private void OnConnected(object? sender, EventArgs e) => SendConnect();

    private void OnDisconnected(object? sender, EventArgs e)
    {
        // The server resends the full list after we re-subscribe.
        lock (_gate)
        {
            _challenges.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessages(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        lock (_gate)
        {
            foreach (JsonElement m in data.EnumerateArray())
            {
                if (OgsJson.Long(m, "challenge_id") is not { } id)
                {
                    continue;
                }

                if (OgsJson.Bool(m, "delete") == true || OgsJson.Bool(m, "game_started") == true)
                {
                    _challenges.Remove(id);
                }
                else if (Parse(m) is { } challenge)
                {
                    _challenges[id] = challenge;
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Keeps a live/blitz challenge alive while waiting for an opponent. The server cancels live challenges whose
/// creator stops sending <c>challenge/keepalive</c> (the web client sends one per second).
/// </summary>
public static class ChallengeKeepAlive
{
    /// <summary>True when the game starts (its gamedata arrives); false when cancelled.</summary>
    public static async Task<bool> WaitForOpponentAsync(
        OgsRealtimeClient client, long challengeId, long gameId, TimeSpan interval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable sub = client.Subscribe($"game/{gameId}/gamedata", _ => started.TrySetResult(true));
        client.Send("game/connect", new JsonObject { ["game_id"] = gameId, ["chat"] = false });

        try
        {
            while (true)
            {
                client.Send("challenge/keepalive", new JsonObject { ["challenge_id"] = challengeId, ["game_id"] = gameId });
                Task delay = Task.Delay(interval, cancellationToken);
                if (await Task.WhenAny(started.Task, delay) == started.Task)
                {
                    return true;
                }

                await delay;
            }
        }
        catch (OperationCanceledException)
        {
            client.Send("game/disconnect", new JsonObject { ["game_id"] = gameId });
            return false;
        }
    }
}
