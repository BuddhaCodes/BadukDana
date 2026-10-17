using Hoshi.Core;
using Hoshi.Sgf;

namespace Hoshi.App.Services;

/// <summary>Which results to keep in a search of the game library.</summary>
public enum ResultFilter
{
    Any,
    BlackWins,
    WhiteWins,
    NoResult,
}

/// <summary>A search of the game library: a player, dates, the result and a pattern on the board (all optional).</summary>
public sealed record GameSearchQuery(string? Player = null, DateTimeOffset? From = null, DateTimeOffset? To = null, ResultFilter Result = ResultFilter.Any, BoardPattern? Pattern = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Player) && From is null && To is null && Result == ResultFilter.Any && Pattern is null;
}

/// <summary>Searching the played games: the index fields first, then (for a pattern) each game's main line.</summary>
public static class GameSearch
{
    /// <summary>Player (part of either name), date range (both days included) and result.</summary>
    public static bool MatchesInfo(ReplayEntry entry, GameSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(query);
        if (!string.IsNullOrWhiteSpace(query.Player)
            && !entry.Black.Contains(query.Player.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && !entry.White.Contains(query.Player.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        DateTime day = entry.Played.ToLocalTime().Date;
        if ((query.From is { } from && day < from.Date) || (query.To is { } to && day > to.Date))
        {
            return false;
        }

        string result = entry.Result?.Trim() ?? string.Empty;
        return query.Result switch
        {
            ResultFilter.BlackWins => result.StartsWith("B+", StringComparison.OrdinalIgnoreCase),
            ResultFilter.WhiteWins => result.StartsWith("W+", StringComparison.OrdinalIgnoreCase),
            ResultFilter.NoResult => result.Length == 0 || result == "?" || result.StartsWith("Void", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    /// <summary>The first move of the main line after which the pattern is on the board, or null.</summary>
    public static int? FindPattern(GameTree tree, IReadOnlyList<BoardPattern> variants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count == 0 || variants[0].BoardWidth != tree.Info.Width || variants[0].BoardHeight != tree.Info.Height)
        {
            return null;
        }

        var cursor = new GameCursor(tree);
        int move = 0;
        for (GameNode node = tree.Root; ; node = node.Children[0])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.HasMove)
            {
                move++;
            }

            BoardState board = cursor.GetBoard(node);
            if (BoardPattern.FindIn(board, variants) is not null)
            {
                return move;
            }

            if (node.Children.Count == 0)
            {
                return null;
            }
        }
    }
}
