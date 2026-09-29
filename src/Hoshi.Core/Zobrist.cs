namespace Hoshi.Core;

/// <summary>
/// Deterministic Zobrist keys (SplitMix64 with a fixed seed), so hashes are stable across runs and processes.
/// Keys are indexed with a fixed stride of <see cref="BoardState.MaxSize"/>, independent of board width.
/// </summary>
internal static class Zobrist
{
    private static readonly ulong[] Keys = CreateKeys();

    /// <summary>XOR-ed into the situational key when White is to move.</summary>
    public static ulong WhiteToMove { get; } = Keys[^1];

    public static ulong Key(int x, int y, Stone stone) =>
        stone == Stone.Empty ? 0UL : Keys[((y * BoardState.MaxSize) + x) * 2 + ((int)stone - 1)];

    private static ulong[] CreateKeys()
    {
        var keys = new ulong[(BoardState.MaxSize * BoardState.MaxSize * 2) + 1];
        ulong state = 0x486F736869476F21UL; // "HoshiGo!"
        for (int i = 0; i < keys.Length; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            keys[i] = z ^ (z >> 31);
        }

        return keys;
    }
}
