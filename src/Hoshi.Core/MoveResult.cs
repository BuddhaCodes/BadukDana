namespace Hoshi.Core;

public enum IllegalMoveReason
{
    OutOfBounds,
    Occupied,
    Suicide,
    Ko,
    Superko,
}

/// <summary>Outcome of <see cref="BoardState.TryPlay"/>.</summary>
public sealed class MoveResult
{
    private MoveResult(BoardState? state, IllegalMoveReason? reason, IReadOnlyList<Point> captured)
    {
        State = state;
        Reason = reason;
        Captured = captured;
    }

    /// <summary>The position after the move, or null when the move is illegal.</summary>
    public BoardState? State { get; }

    public IllegalMoveReason? Reason { get; }

    /// <summary>
    /// Stones removed by the move. Normally opponent stones; with suicide allowed, the mover's own stones.
    /// </summary>
    public IReadOnlyList<Point> Captured { get; }

    public bool IsLegal => State is not null;

    /// <summary>A legal result for a pass (no captures), for callers that treat moves and passes uniformly.</summary>
    public static MoveResult ForPass(BoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new MoveResult(state, null, []);
    }

    internal static MoveResult Legal(BoardState state, IReadOnlyList<Point> captured) => new(state, null, captured);

    internal static MoveResult Illegal(IllegalMoveReason reason) => new(null, reason, []);
}

public sealed class IllegalMoveException : InvalidOperationException
{
    public IllegalMoveException()
    {
    }

    public IllegalMoveException(string message)
        : base(message)
    {
    }

    public IllegalMoveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IllegalMoveException(Stone color, Point point, IllegalMoveReason reason)
        : base($"{color} at {point} is illegal: {reason}.")
    {
        Reason = reason;
    }

    public IllegalMoveReason Reason { get; }
}
