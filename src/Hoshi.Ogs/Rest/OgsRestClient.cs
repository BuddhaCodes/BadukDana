using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Ogs.Auth;

namespace Hoshi.Ogs.Rest;

/// <summary>
/// The REST endpoints (prefix <c>/api/v1/</c>) Hoshi uses. Routes are the ones the official web client calls
/// (online-go.com <c>src/</c>, verified 2026-09-29).
/// </summary>
public sealed class OgsRestClient
{
    private readonly HttpClient _http;
    private readonly IOgsCredentials _credentials;

    public OgsRestClient(HttpClient http, IOgsCredentials credentials)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    /// <summary>The signed-in user's ongoing games (<c>GET ui/overview</c>, as on the web home page).</summary>
    public async Task<IReadOnlyList<OgsActiveGame>> GetActiveGamesAsync(CancellationToken cancellationToken)
    {
        using JsonDocument doc = await SendAsync(HttpMethod.Get, "ui/overview", null, cancellationToken);
        var games = new List<OgsActiveGame>();
        if (doc.RootElement.TryGetProperty("active_games", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement g in list.EnumerateArray())
            {
                JsonElement json = g.TryGetProperty("json", out JsonElement j) ? j : default;
                games.Add(new OgsActiveGame(
                    OgsJson.Long(g, "id") ?? 0,
                    OgsJson.String(g, "name") ?? string.Empty,
                    OgsJson.ReadUser(g.GetProperty("black")),
                    OgsJson.ReadUser(g.GetProperty("white")),
                    (int)(OgsJson.Long(g, "width") ?? 19),
                    (int)(OgsJson.Long(g, "height") ?? 19),
                    json.ValueKind == JsonValueKind.Object ? OgsJson.Long(json, "player_to_move") : null,
                    json.ValueKind == JsonValueKind.Object ? OgsJson.String(json, "phase") ?? "play" : "play"));
            }
        }

        return games;
    }

    /// <summary>Creates an open challenge, or a direct one when <paramref name="opponentId"/> is given.</summary>
    public async Task<CreatedChallenge> CreateChallengeAsync(ChallengeRequest request, long? opponentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = opponentId is { } id
            ? string.Create(CultureInfo.InvariantCulture, $"players/{id}/challenge")
            : "challenges";
        using JsonDocument doc = await SendAsync(HttpMethod.Post, path, request.ToJson(), cancellationToken);
        JsonElement root = doc.RootElement;
        string speed = request.TimeControl.SpeedFor(request.Width, request.Height);
        return new CreatedChallenge(
            OgsJson.Long(root, "challenge") ?? OgsJson.Long(root, "id") ?? 0,
            OgsJson.Long(root, "game") ?? 0,
            IsLive: speed is "live" or "blitz");
    }

    /// <summary>Cancels one of our own challenges (<c>DELETE me/challenges/{id}</c>).</summary>
    public async Task CancelChallengeAsync(long challengeId, CancellationToken cancellationToken)
    {
        using JsonDocument _ = await SendAsync(
            HttpMethod.Delete, string.Create(CultureInfo.InvariantCulture, $"me/challenges/{challengeId}"), null, cancellationToken);
    }

    /// <summary>Accepts an open challenge; returns the game id when the server reports it (0 otherwise).</summary>
    public async Task<long> AcceptChallengeAsync(long challengeId, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await SendAsync(
            HttpMethod.Post, string.Create(CultureInfo.InvariantCulture, $"challenges/{challengeId}/accept"), new JsonObject(), cancellationToken);
        return OgsJson.Long(doc.RootElement, "game") ?? OgsJson.Long(doc.RootElement, "game_id") ?? 0;
    }

    /// <summary>Looks a player up by exact username (<c>GET players?username=</c>).</summary>
    public async Task<OgsUser?> FindPlayerAsync(string username, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        using JsonDocument doc = await SendAsync(
            HttpMethod.Get, "players?username=" + Uri.EscapeDataString(username.Trim()), null, cancellationToken);
        return doc.RootElement.TryGetProperty("results", out JsonElement results)
            && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0
                ? OgsJson.ReadUser(results[0])
                : null;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_http.BaseAddress!, "/api/v1/" + path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        await _credentials.ApplyAsync(request, cancellationToken);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        (_credentials as OgsAuthService)?.StoreCookies(response);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new OgsApiException(response.StatusCode, ErrorMessage(response.StatusCode, text));
        }

        return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
    }

    private static string ErrorMessage(HttpStatusCode status, string text)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            foreach (string key in new[] { "detail", "error", "errors", "message" })
            {
                if (OgsJson.String(doc.RootElement, key) is { Length: > 0 } m)
                {
                    return m;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (HTML error page, etc.).
        }

        return string.Create(CultureInfo.InvariantCulture, $"OGS respondió {(int)status} {status}.");
    }
}
