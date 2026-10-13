using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hoshi.Ogs.Realtime;

/// <summary>
/// What to look for with OGS's automatch: board size (9, 13, 19), speed ("blitz", "rapid", "live",
/// "correspondence"), time system ("byoyomi", "fischer"), rules (preferred, not required) and how far the
/// opponent's rank may be. Automatch games on OGS are always ranked.
/// </summary>
public sealed record OgsAutomatchRequest(int Size, string Speed, string System, string Rules, bool AllowHandicap, int RankRange)
{
    /// <summary>The <c>automatch/find_match</c> payload (goban <c>AutomatchPreferences</c>).</summary>
    public JsonObject ToJson(string uuid) => new()
    {
        ["uuid"] = uuid,
        ["size_speed_options"] = new JsonArray(new JsonObject
        {
            ["size"] = $"{Size}x{Size}",
            ["speed"] = Speed,
            ["system"] = System,
        }),
        ["lower_rank_diff"] = RankRange,
        ["upper_rank_diff"] = RankRange,
        ["rules"] = new JsonObject { ["condition"] = "preferred", ["value"] = Rules },
        ["handicap"] = new JsonObject { ["condition"] = "preferred", ["value"] = AllowHandicap ? "enabled" : "disabled" },
    };
}

/// <summary>An automatch search found an opponent and the game started.</summary>
public sealed record OgsAutomatchStarted(string Uuid, long GameId);

/// <summary>
/// OGS's automatch over the realtime socket (goban protocol: <c>automatch/find_match</c>, <c>automatch/cancel</c>;
/// the server answers <c>automatch/entry</c>, <c>automatch/start</c> and <c>automatch/cancel</c>).
/// </summary>
public sealed class OgsAutomatch : IDisposable
{
    private readonly OgsRealtimeClient _client;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<string> _searching = [];
    private readonly object _gate = new();

    public OgsAutomatch(OgsRealtimeClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _subscriptions.Add(client.Subscribe("automatch/start", OnStart));
        _subscriptions.Add(client.Subscribe("automatch/cancel", OnCancel));
        _subscriptions.Add(client.Subscribe("automatch/entry", OnEntry));
    }

    public event EventHandler<OgsAutomatchStarted>? Started;

    /// <summary>Raised when the searches change (started, found, cancelled).</summary>
    public event EventHandler? Changed;

    /// <summary>Searches waiting for an opponent.</summary>
    public IReadOnlyCollection<string> Searching
    {
        get
        {
            lock (_gate)
            {
                return [.. _searching];
            }
        }
    }

    /// <summary>Starts a search; returns its id (for <see cref="Cancel"/>).</summary>
    public string Find(OgsAutomatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string uuid = Guid.NewGuid().ToString();
        lock (_gate)
        {
            _searching.Add(uuid);
        }

        _client.Send("automatch/find_match", request.ToJson(uuid));
        Changed?.Invoke(this, EventArgs.Empty);
        return uuid;
    }

    public void Cancel(string uuid)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        lock (_gate)
        {
            _searching.Remove(uuid);
        }

        _client.Send("automatch/cancel", new JsonObject { ["uuid"] = uuid });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (IDisposable s in _subscriptions)
        {
            s.Dispose();
        }
    }

    private void OnStart(JsonElement data)
    {
        if (OgsJson.String(data, "uuid") is not { } uuid || OgsJson.Long(data, "game_id") is not { } game)
        {
            return;
        }

        lock (_gate)
        {
            _searching.Remove(uuid);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        Started?.Invoke(this, new OgsAutomatchStarted(uuid, game));
    }

    private void OnCancel(JsonElement data)
    {
        lock (_gate)
        {
            if (OgsJson.String(data, "uuid") is { } uuid)
            {
                _searching.Remove(uuid);
            }
            else
            {
                _searching.Clear(); // null cancels them all
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntry(JsonElement data)
    {
        if (OgsJson.String(data, "uuid") is { } uuid)
        {
            lock (_gate)
            {
                _searching.Add(uuid);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>One of the player's games as <c>active_game</c> reports it: whose turn, phase, players.</summary>
public sealed record OgsGameTurn(long GameId, string Phase, long PlayerToMove, string Black, string White, string Name, bool IsCorrespondence);

/// <summary>
/// The player's games as the server pushes them (<c>active_game</c>, goban <c>GameListEntry</c>): used to tell the
/// player it is their turn in another game.
/// </summary>
public sealed class OgsActiveGames : IDisposable
{
    /// <summary>Above this many seconds per move a game is a correspondence game.</summary>
    public const int CorrespondenceSecondsPerMove = 3600;

    private readonly IDisposable _subscription;

    public OgsActiveGames(OgsRealtimeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _subscription = client.Subscribe("active_game", OnActiveGame);
    }

    public event EventHandler<OgsGameTurn>? Updated;

    public void Dispose() => _subscription.Dispose();

    internal static OgsGameTurn? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || OgsJson.Long(e, "id") is not { } id)
        {
            return null;
        }

        string Player(string color) =>
            e.TryGetProperty(color, out JsonElement p) && p.ValueKind == JsonValueKind.Object ? OgsJson.String(p, "username") ?? "?" : "?";

        return new OgsGameTurn(
            id,
            OgsJson.String(e, "phase") ?? "play",
            OgsJson.Long(e, "player_to_move") ?? 0,
            Player("black"),
            Player("white"),
            OgsJson.String(e, "name") ?? string.Empty,
            (OgsJson.Long(e, "time_per_move") ?? 0) >= CorrespondenceSecondsPerMove);
    }

    private void OnActiveGame(JsonElement data)
    {
        if (Parse(data) is { } turn)
        {
            Updated?.Invoke(this, turn);
        }
    }
}
