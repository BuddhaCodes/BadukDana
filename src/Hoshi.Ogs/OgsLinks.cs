using System.Globalization;
using System.Text.RegularExpressions;

namespace Hoshi.Ogs;

/// <summary>Game numbers from what people paste: "12345", "#12345", "https://online-go.com/game/12345", review links…</summary>
public static partial class OgsLinks
{
    [GeneratedRegex(@"^\s*#?(\d{1,12})\s*$")]
    private static partial Regex Number();

    [GeneratedRegex(@"(?:online-go\.com|ogs\.)[^\s]*/game/(?:view/)?(\d{1,12})", RegexOptions.IgnoreCase)]
    private static partial Regex GameUrl();

    /// <summary>The game number in a pasted text, or null.</summary>
    public static long? TryGameId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match m = Number().Match(text);
        if (!m.Success)
        {
            m = GameUrl().Match(text);
        }

        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 ? id : null;
    }

    /// <summary>The web page of a game on a server.</summary>
    public static Uri GameUri(Uri server, long gameId) =>
        new(server, string.Create(CultureInfo.InvariantCulture, $"/game/{gameId}"));
}
