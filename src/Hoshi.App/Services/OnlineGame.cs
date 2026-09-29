using Hoshi.Core;
using Hoshi.Ogs.Games;
using Hoshi.Ogs.Realtime;

namespace Hoshi.App.Services;

/// <summary>An online game as the UI sees it (an <see cref="OgsGameSession"/> in production, a fake in tests).</summary>
public interface IOnlineGame : IDisposable
{
    long GameId { get; }

    /// <summary>The signed-in user's player id (0 when watching).</summary>
    long MyPlayerId { get; }

    /// <summary>Server time now (local clock corrected with the socket's drift).</summary>
    DateTimeOffset ServerNow { get; }

    event EventHandler<OgsGameSnapshot>? GamedataReceived;

    event EventHandler<OgsGameMove>? MoveReceived;

    event EventHandler<OgsClock>? ClockChanged;

    event EventHandler<OgsGamePhase>? PhaseChanged;

    event EventHandler<IReadOnlyList<Point>>? RemovedStonesChanged;

    event EventHandler<OgsGameResult>? GameEnded;

    event EventHandler<OgsChatLine>? ChatReceived;

    event EventHandler<string>? ErrorReceived;

    event EventHandler<int>? UndoRequested;

    event EventHandler<int>? UndoAccepted;

    void Connect();

    void Play(Point? point);

    void Resign();

    void SendChat(string body);

    void RequestUndo();

    void AcceptUndo();

    void SetRemovedStones(IEnumerable<Point> stones, bool removed);

    void AcceptRemovedStones(IEnumerable<Point> allRemoved);

    void RejectRemovedStones();
}

public sealed class OgsOnlineGame : IOnlineGame
{
    private readonly OgsGameSession _session;
    private readonly OgsRealtimeClient _realtime;

    public OgsOnlineGame(OgsGameSession session, OgsRealtimeClient realtime, long myPlayerId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _realtime = realtime ?? throw new ArgumentNullException(nameof(realtime));
        MyPlayerId = myPlayerId;
    }

    public event EventHandler<OgsGameSnapshot>? GamedataReceived
    {
        add => _session.GamedataReceived += value;
        remove => _session.GamedataReceived -= value;
    }

    public event EventHandler<OgsGameMove>? MoveReceived
    {
        add => _session.MoveReceived += value;
        remove => _session.MoveReceived -= value;
    }

    public event EventHandler<OgsClock>? ClockChanged
    {
        add => _session.ClockChanged += value;
        remove => _session.ClockChanged -= value;
    }

    public event EventHandler<OgsGamePhase>? PhaseChanged
    {
        add => _session.PhaseChanged += value;
        remove => _session.PhaseChanged -= value;
    }

    public event EventHandler<IReadOnlyList<Point>>? RemovedStonesChanged
    {
        add => _session.RemovedStonesChanged += value;
        remove => _session.RemovedStonesChanged -= value;
    }

    public event EventHandler<OgsGameResult>? GameEnded
    {
        add => _session.GameEnded += value;
        remove => _session.GameEnded -= value;
    }

    public event EventHandler<OgsChatLine>? ChatReceived
    {
        add => _session.ChatReceived += value;
        remove => _session.ChatReceived -= value;
    }

    public event EventHandler<string>? ErrorReceived
    {
        add => _session.ErrorReceived += value;
        remove => _session.ErrorReceived -= value;
    }

    public event EventHandler<int>? UndoRequested
    {
        add => _session.UndoRequested += value;
        remove => _session.UndoRequested -= value;
    }

    public event EventHandler<int>? UndoAccepted
    {
        add => _session.UndoAccepted += value;
        remove => _session.UndoAccepted -= value;
    }

    public long GameId => _session.GameId;

    public long MyPlayerId { get; }

    public DateTimeOffset ServerNow => _realtime.ServerNow;

    public void Connect() => _session.Connect();

    public void Play(Point? point) => _session.Play(point);

    public void Resign() => _session.Resign();

    public void SendChat(string body) => _session.SendChat(body);

    public void RequestUndo() => _session.RequestUndo();

    public void AcceptUndo() => _session.AcceptUndo();

    public void SetRemovedStones(IEnumerable<Point> stones, bool removed) => _session.SetRemovedStones(stones, removed);

    public void AcceptRemovedStones(IEnumerable<Point> allRemoved) => _session.AcceptRemovedStones(allRemoved);

    public void RejectRemovedStones() => _session.RejectRemovedStones();

    public void Dispose() => _session.Dispose();
}
