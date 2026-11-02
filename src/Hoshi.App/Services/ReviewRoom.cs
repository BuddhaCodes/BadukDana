using Hoshi.Core;
using Hoshi.Ogs.Realtime;
using Hoshi.Ogs.Reviews;

namespace Hoshi.App.Services;

/// <summary>A review room on OGS, as the app sees it (teacher mode).</summary>
public interface IOgsReviewRoom : IDisposable
{
    long ReviewId { get; }

    /// <summary>The signed-in player (who owns the rooms Hoshi opens).</summary>
    long MyPlayerId { get; }

    event EventHandler<IReadOnlyList<OgsReviewMessage>>? FullState;

    event EventHandler<OgsReviewMessage>? Appended;

    void Connect();

    void Show(int from, IReadOnlyList<Point?> moves, string text, IReadOnlyDictionary<string, IReadOnlyList<Point>> marks);

    void Chat(int from, IReadOnlyList<Point?> moves, string body);
}

internal sealed class OgsReviewRoom(OgsRealtimeClient realtime, long reviewId, long myPlayerId) : IOgsReviewRoom
{
    private readonly OgsReviewSession _session = new(realtime, reviewId);

    public long ReviewId => _session.ReviewId;

    public long MyPlayerId { get; } = myPlayerId;

    public event EventHandler<IReadOnlyList<OgsReviewMessage>>? FullState
    {
        add => _session.FullState += value;
        remove => _session.FullState -= value;
    }

    public event EventHandler<OgsReviewMessage>? Appended
    {
        add => _session.Appended += value;
        remove => _session.Appended -= value;
    }

    public void Connect() => _session.Connect();

    public void Show(int from, IReadOnlyList<Point?> moves, string text, IReadOnlyDictionary<string, IReadOnlyList<Point>> marks) =>
        _session.Show(from, moves, text, marks);

    public void Chat(int from, IReadOnlyList<Point?> moves, string body) => _session.Chat(from, moves, body);

    public void Dispose() => _session.Dispose();
}
