using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Core;
using Hoshi.Ogs.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Ogs.Games;

/// <summary>
/// One online game over <see cref="OgsRealtimeClient"/>, without any knowledge of the SGF tree (CLAUDE.md §3):
/// it turns <c>game/{id}/…</c> events into domain events with Core types and sends the player's commands.
/// After every reconnection it sends <c>game/connect</c> again; the server answers with a fresh <c>gamedata</c>.
/// Events are raised on the socket thread.
/// </summary>
public sealed class OgsGameSession : IDisposable
{
    private readonly OgsRealtimeClient _client;
    private readonly ILogger _logger;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _gate = new();
    private OgsGameSnapshot? _snapshot;
    private List<OgsGameMove> _moves = [];
    private bool _disposed;

    public OgsGameSession(OgsRealtimeClient client, long gameId, ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        GameId = gameId;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Full state (first connection and after every reconnection). Replaces all previous state.</summary>
    public event EventHandler<OgsGameSnapshot>? GamedataReceived;

    public event EventHandler<OgsGameMove>? MoveReceived;

    public event EventHandler<OgsClock>? ClockChanged;

    public event EventHandler<OgsGamePhase>? PhaseChanged;

    /// <summary>All stones currently marked dead (or dame) during stone removal.</summary>
    public event EventHandler<IReadOnlyList<Point>>? RemovedStonesChanged;

    public event EventHandler<OgsGameResult>? GameEnded;

    public event EventHandler<OgsChatLine>? ChatReceived;

    /// <summary>Chat lines a moderator removed (their chat ids).</summary>
    public event EventHandler<IReadOnlyList<string>>? ChatRemoved;

    /// <summary>Server error for this game (e.g. a rejected move), already human readable.</summary>
    public event EventHandler<string>? ErrorReceived;

    /// <summary>The opponent asks to undo back to this move number.</summary>
    public event EventHandler<int>? UndoRequested;

    /// <summary>An undo was accepted; the game continues from this move number.</summary>
    public event EventHandler<int>? UndoAccepted;

    public long GameId { get; }

    public OgsGameSnapshot? Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot is null ? null : _snapshot with { Moves = [.. _moves] };
            }
        }
    }

    public int MoveCount
    {
        get
        {
            lock (_gate)
            {
                return _moves.Count;
            }
        }
    }

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string p = $"game/{GameId}/";
        _subscriptions.Add(_client.Subscribe(p + "gamedata", OnGamedata));
        _subscriptions.Add(_client.Subscribe(p + "move", OnMove));
        _subscriptions.Add(_client.Subscribe(p + "clock", d => ClockChanged?.Invoke(this, OgsGameParser.ParseClock(d))));
        _subscriptions.Add(_client.Subscribe(p + "phase", OnPhase));
        _subscriptions.Add(_client.Subscribe(p + "removed_stones", OnRemovedStones));
        _subscriptions.Add(_client.Subscribe(p + "removed_stones_accepted", OnRemovedStonesAccepted));
        _subscriptions.Add(_client.Subscribe(p + "chat", d =>
        {
            if (OgsGameParser.ParseChat(d) is { } line)
            {
                ChatReceived?.Invoke(this, line);
            }
        }));
        _subscriptions.Add(_client.Subscribe(p + "chat/remove", d => ChatRemoved?.Invoke(this, OgsGameParser.ParseChatRemoval(d))));
        _subscriptions.Add(_client.Subscribe(p + "error", d => ErrorReceived?.Invoke(this, d.ValueKind == JsonValueKind.String ? d.GetString() ?? "Error" : d.ToString())));
        _subscriptions.Add(_client.Subscribe(p + "undo_requested", d => UndoRequested?.Invoke(this, MoveNumberOf(d))));
        _subscriptions.Add(_client.Subscribe(p + "undo_accepted", OnUndoAccepted));
        _client.Connected += OnReconnected;
        SendConnect();
    }

    /// <summary>Plays a stone, or passes when <paramref name="point"/> is null. The server echoes it as a move event.</summary>
    public void Play(Point? point) =>
        _client.Send("game/move", new JsonObject { ["game_id"] = GameId, ["move"] = point?.ToSgf() ?? ".." });

    public void Resign() => _client.Send("game/resign", new JsonObject { ["game_id"] = GameId });

    /// <summary>Cancels the game (only allowed in its first moves).</summary>
    public void Cancel() => _client.Send("game/cancel", new JsonObject { ["game_id"] = GameId });

    public void SendChat(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        _client.Send("game/chat", new JsonObject
        {
            ["game_id"] = GameId,
            ["body"] = body.Trim(),
            ["type"] = "main",
            ["move_number"] = MoveCount,
        });
    }

    /// <summary>
    /// Sends a phrase in several languages (<c>{type: "translated", en: …, es: …}</c>, goban
    /// <c>GameChatTranslatedMessage</c>): every player reads it in their own language. "en" is required.
    /// </summary>
    public void SendTranslatedChat(IReadOnlyDictionary<string, string> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        if (!phrases.ContainsKey("en"))
        {
            throw new ArgumentException("A translated chat message needs an English text.", nameof(phrases));
        }

        var body = new JsonObject { ["type"] = "translated" };
        foreach ((string language, string text) in phrases)
        {
            body[language] = text;
        }

        _client.Send("game/chat", new JsonObject
        {
            ["game_id"] = GameId,
            ["body"] = body,
            ["type"] = "main",
            ["move_number"] = MoveCount,
        });
    }

    public void RequestUndo() =>
        _client.Send("game/undo/request", new JsonObject { ["game_id"] = GameId, ["move_number"] = MoveCount });

    public void AcceptUndo() =>
        _client.Send("game/undo/accept", new JsonObject { ["game_id"] = GameId, ["move_number"] = MoveCount });

    /// <summary>Marks (or unmarks) stones as dead during stone removal.</summary>
    public void SetRemovedStones(IEnumerable<Point> stones, bool removed)
    {
        ArgumentNullException.ThrowIfNull(stones);
        _client.Send("game/removed_stones/set", new JsonObject
        {
            ["game_id"] = GameId,
            ["removed"] = removed,
            ["stones"] = OgsGameParser.Encode(stones),
        });
    }

    /// <summary>Accepts the current dead stones; the game ends once both players accept the same set.</summary>
    public void AcceptRemovedStones(IEnumerable<Point> allRemoved)
    {
        ArgumentNullException.ThrowIfNull(allRemoved);
        _client.Send("game/removed_stones/accept", new JsonObject
        {
            ["game_id"] = GameId,
            ["stones"] = OgsGameParser.Encode(allRemoved.OrderBy(p => p.Y).ThenBy(p => p.X)),
            ["strict_seki_mode"] = false,
        });
    }

    /// <summary>Rejects the dead stones and resumes play.</summary>
    public void RejectRemovedStones() =>
        _client.Send("game/removed_stones/reject", new JsonObject { ["game_id"] = GameId });

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

        _subscriptions.Clear();
        _client.Send("game/disconnect", new JsonObject { ["game_id"] = GameId });
    }

    private void SendConnect() =>
        _client.Send("game/connect", new JsonObject { ["game_id"] = GameId, ["chat"] = true });

    private void OnReconnected(object? sender, EventArgs e) => SendConnect();

    private void OnGamedata(JsonElement data)
    {
        OgsGameSnapshot snapshot = OgsGameParser.ParseGamedata(data);
        if (snapshot.GameId == 0)
        {
            snapshot = snapshot with { GameId = GameId };
        }

        lock (_gate)
        {
            _snapshot = snapshot;
            _moves = [.. snapshot.Moves];
        }

        _logger.LogInformation("Game {GameId}: gamedata with {Moves} moves, phase {Phase}", GameId, snapshot.Moves.Count, snapshot.Phase);
        GamedataReceived?.Invoke(this, snapshot);

        // Resignation, timeout and cancellation arrive as a new gamedata with the outcome.
        if (snapshot.Phase == OgsGamePhase.Finished && snapshot.Result is { } result)
        {
            GameEnded?.Invoke(this, result);
        }
    }

    private void OnMove(JsonElement data)
    {
        if (!data.TryGetProperty("move", out JsonElement m) || !OgsGameParser.TryParseMove(m, out Point? point, out Stone explicitColor))
        {
            _logger.LogWarning("Game {GameId}: unreadable move event", GameId);
            return;
        }

        OgsGameMove move;
        lock (_gate)
        {
            if (_snapshot is null)
            {
                return; // gamedata will include it.
            }

            int index = _moves.Count;
            if (point is null && _snapshot.FreeHandicapPlacement && index < _snapshot.Handicap)
            {
                return;
            }

            Stone color = explicitColor != Stone.Empty ? explicitColor : _snapshot.ColorForMove(index);
            move = new OgsGameMove(index + 1, color, point);
            _moves.Add(move);
        }

        _logger.LogDebug("Game {GameId}: move {Number} (server move_number {ServerNumber})", GameId, move.Number, OgsJson.Long(data, "move_number"));
        MoveReceived?.Invoke(this, move);
    }

    private void OnPhase(JsonElement data)
    {
        OgsGamePhase phase = OgsGameParser.Phase(data.ValueKind == JsonValueKind.String ? data.GetString() : null);
        lock (_gate)
        {
            if (_snapshot is not null)
            {
                _snapshot = _snapshot with { Phase = phase };
            }
        }

        PhaseChanged?.Invoke(this, phase);
    }

    private void OnRemovedStones(JsonElement data)
    {
        if (OgsJson.String(data, "all_removed") is not { } all)
        {
            return; // strict_seki_mode toggle only.
        }

        IReadOnlyList<Point> removed = OgsGameParser.Points(all);
        lock (_gate)
        {
            if (_snapshot is not null)
            {
                _snapshot = _snapshot with { Removed = removed };
            }
        }

        RemovedStonesChanged?.Invoke(this, removed);
    }

    private void OnRemovedStonesAccepted(JsonElement data)
    {
        OgsGamePhase phase = OgsGameParser.Phase(OgsJson.String(data, "phase"));
        OgsGameSnapshot? s = Snapshot;
        if (phase != OgsGamePhase.Finished || s is null)
        {
            return; // One player accepted; waiting for the other.
        }

        OgsGameResult result = OgsGameParser.ParseResult(data, s.Black.Id, s.White.Id)
            ?? new OgsGameResult(Stone.Empty, "?");
        lock (_gate)
        {
            _snapshot = _snapshot! with { Phase = OgsGamePhase.Finished, Result = result };
        }

        PhaseChanged?.Invoke(this, OgsGamePhase.Finished);
        GameEnded?.Invoke(this, result);
    }

    private void OnUndoAccepted(JsonElement data)
    {
        int number = MoveNumberOf(data);
        lock (_gate)
        {
            if (number >= 0 && number < _moves.Count)
            {
                _moves.RemoveRange(number, _moves.Count - number);
            }
        }

        UndoAccepted?.Invoke(this, number);
    }

    private static int MoveNumberOf(JsonElement d) => d.ValueKind == JsonValueKind.Number
        ? d.GetInt32()
        : (int)(OgsJson.Long(d, "move_number") ?? -1);
}
