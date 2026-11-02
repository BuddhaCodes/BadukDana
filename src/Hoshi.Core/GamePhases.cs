namespace Hoshi.Core;

/// <summary>The three parts of a game, for "what should I study?".</summary>
public enum GamePhase
{
    Opening,
    Middle,
    Endgame,
}

/// <summary>
/// Which part of the game a move belongs to, by move number scaled to the board's area: the opening is roughly the
/// first 14 % of the points (move 51 on 19×19, 24 on 13×13, 11 on 9×9), the endgame starts after 45 % (move 162 on
/// 19×19, 76 on 13×13, 36 on 9×9). A rule of thumb, not a reading of the position, but it is the same for every game
/// and so comparable month to month.
/// </summary>
public static class GamePhases
{
    public static int OpeningEnd(int size) => (int)Math.Round(0.14 * size * size);

    public static int EndgameStart(int size) => (int)Math.Round(0.45 * size * size);

    /// <summary>The phase of move <paramref name="moveNumber"/> (1 = the first move) on a board of <paramref name="size"/>.</summary>
    public static GamePhase Of(int moveNumber, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        return moveNumber <= OpeningEnd(size) ? GamePhase.Opening
            : moveNumber > EndgameStart(size) ? GamePhase.Endgame
            : GamePhase.Middle;
    }
}
