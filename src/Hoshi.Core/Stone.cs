namespace Hoshi.Core;

/// <summary>Content of an intersection.</summary>
public enum Stone : byte
{
    Empty = 0,
    Black = 1,
    White = 2,
}

public static class StoneExtensions
{
    /// <summary>The other colour; <see cref="Stone.Empty"/> maps to itself.</summary>
    public static Stone Opponent(this Stone stone) => stone switch
    {
        Stone.Black => Stone.White,
        Stone.White => Stone.Black,
        _ => Stone.Empty,
    };
}
