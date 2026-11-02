using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hoshi.Ogs.Realtime;

/// <summary>
/// Development aid (off by default; <c>--Ogs:RecordTraffic=true</c>): writes what Hoshi exchanges with OGS — every
/// WebSocket frame and every REST response — to a JSON Lines file, so real traffic from beta.online-go.com can become
/// test fixtures. Secrets never reach the file: JWTs, tokens, passwords, auth values and cookies are replaced by
/// <c>[redacted]</c> before writing (<see cref="Scrub"/>).
/// </summary>
public sealed partial class OgsTrafficRecorder : IDisposable
{
    private static readonly string[] SecretWords = ["jwt", "token", "password", "secret", "auth", "cookie", "csrf", "verifier", "apikey", "api_key", "session"];
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public OgsTrafficRecorder(string directory, string server)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"ogs-{Safe(server)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        Write(new JsonObject { ["k"] = "start", ["server"] = server, ["utc"] = DateTimeOffset.UtcNow });
    }

    /// <summary>The capture being written.</summary>
    public string FilePath { get; }

    /// <summary>A WebSocket frame: <paramref name="outgoing"/> for what Hoshi sent.</summary>
    public void Frame(bool outgoing, string text) =>
        Write(new JsonObject { ["k"] = "ws", ["d"] = outgoing ? "out" : "in", ["data"] = Parse(text) });

    /// <summary>A REST exchange (the body only when it is JSON).</summary>
    public void Http(string method, string pathAndQuery, int status, string? body) =>
        Write(new JsonObject { ["k"] = "http", ["method"] = method, ["path"] = ScrubQuery(pathAndQuery), ["status"] = status, ["data"] = body is null ? null : Parse(body) });

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    /// <summary>Copies <paramref name="node"/> with every secret-looking value replaced (keys matched case-insensitively).</summary>
    public static JsonNode? Scrub(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, IsSecret(p.Key) && p.Value is JsonValue ? JsonValue.Create("[redacted]") : Scrub(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(Scrub)]),
        JsonValue v when v.TryGetValue(out string? s) && Jwt().IsMatch(s) => JsonValue.Create("[redacted]"),
        _ => node?.DeepClone(),
    };

    /// <summary>Removes secret query parameters (<c>?token=…</c>) from a path.</summary>
    public static string ScrubQuery(string pathAndQuery) =>
        QuerySecret().Replace(pathAndQuery, m => m.Groups[1].Value + "=[redacted]");

    private static bool IsSecret(string key) => SecretWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Parse(string text)
    {
        try
        {
            return Scrub(JsonNode.Parse(text));
        }
        catch (JsonException)
        {
            return JsonValue.Create(Jwt().Replace(text, "[redacted]"));
        }
    }

    private void Write(JsonObject entry)
    {
        entry["t"] = _clock.ElapsedMilliseconds;
        string line = entry.ToJsonString();
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    private static string Safe(string server) => string.Concat(server.Where(char.IsLetterOrDigit));

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"([?&](?:[a-z_]*(?:token|jwt|auth|code|password|secret)[a-z_]*))=[^&#]*", RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecret();
}

/// <summary>A WebSocket that tells <see cref="OgsTrafficRecorder"/> about every frame.</summary>
public sealed class RecordingWebSocketConnection(IWebSocketConnection inner, OgsTrafficRecorder recorder) : IWebSocketConnection
{
    public int? CloseStatus => inner.CloseStatus;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) => inner.ConnectAsync(uri, cancellationToken);

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        recorder.Frame(outgoing: true, message);
        return inner.SendAsync(message, cancellationToken);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        string? message = await inner.ReceiveAsync(cancellationToken);
        if (message is not null)
        {
            recorder.Frame(outgoing: false, message);
        }

        return message;
    }

    public Task CloseAsync(int code, string reason, CancellationToken cancellationToken) => inner.CloseAsync(code, reason, cancellationToken);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

/// <summary>An HTTP handler that tells <see cref="OgsTrafficRecorder"/> about every REST response.</summary>
public sealed class RecordingHttpHandler(OgsTrafficRecorder recorder) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        string? body = null;
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        recorder.Http(request.Method.Method, request.RequestUri?.PathAndQuery ?? string.Empty, (int)response.StatusCode, body);
        return response;
    }
}
