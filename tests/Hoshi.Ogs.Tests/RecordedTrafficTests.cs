using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Ogs.Games;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Tests;

/// <summary>
/// Replays recordings of real OGS traffic (Fixtures/recorded/*.jsonl, made with <c>--Ogs:RecordTraffic=true</c> on
/// beta.online-go.com, see docs/OGS_API.md §6) through <see cref="OgsGameSession"/>: every game in a recording must
/// parse, raise each move once, and end when the server says it ended. <c>synthetic-*</c> files exercise the harness
/// until real captures are added.
/// </summary>
public sealed class RecordedTrafficTests
{
    public static IEnumerable<string> Recordings() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "recorded"), "*.jsonl").Order(StringComparer.Ordinal);

    [Fact]
    public async Task Every_recorded_game_replays_through_the_session()
    {
        var files = Recordings().ToList();
        files.Should().NotBeEmpty("the synthetic sample is always there");
        foreach (string file in files)
        {
            foreach ((long gameId, List<string> frames) in GameFrames(file))
            {
                await ReplayAsync(Path.GetFileName(file), gameId, frames);
            }
        }
    }

    [Fact]
    public void Recordings_never_contain_secrets()
    {
        foreach (string file in Recordings())
        {
            string text = File.ReadAllText(file);
            text.Should().NotMatchRegex(@"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.", $"{Path.GetFileName(file)} must not hold a JWT");
            foreach (string line in File.ReadLines(file))
            {
                AssertNoSecretValues(JsonNode.Parse(line), Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void The_recorder_scrubs_secrets_from_frames_and_queries()
    {
        JsonNode scrubbed = OgsTrafficRecorder.Scrub(JsonNode.Parse(
            """["authenticate",{"jwt":"abc","player_id":5,"nested":{"chat_auth":"x","name":"ok"},"note":"eyJhbGciOi.eyJzdWIiOiIx.c2lnbmF0dXJl"}]"""))!;

        scrubbed.ToJsonString().Should().NotContainAny("abc", "\"x\"", "eyJhbGciOi").And.Contain("\"player_id\":5").And.Contain("\"name\":\"ok\"");
        OgsTrafficRecorder.ScrubQuery("/api/v1/me?access_token=zzz&page=2").Should().Be("/api/v1/me?access_token=[redacted]&page=2");
    }

    private static void AssertNoSecretValues(JsonNode? node, string file)
    {
        switch (node)
        {
            case JsonObject o:
                foreach ((string key, JsonNode? value) in o)
                {
                    if (value is JsonValue v && v.TryGetValue(out string? s)
                        && (key.Contains("jwt", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase)))
                    {
                        s.Should().Be("[redacted]", $"{file}: {key}");
                    }

                    AssertNoSecretValues(value, file);
                }

                break;
            case JsonArray a:
                foreach (JsonNode? item in a)
                {
                    AssertNoSecretValues(item, file);
                }

                break;
        }
    }

    /// <summary>Incoming frames grouped by game: <c>["game/{id}/…", payload]</c>.</summary>
    private static Dictionary<long, List<string>> GameFrames(string file)
    {
        var games = new Dictionary<long, List<string>>();
        foreach (string line in File.ReadLines(file))
        {
            JsonNode? entry = JsonNode.Parse(line);
            if (entry?["k"]?.GetValue<string>() != "ws" || entry["d"]?.GetValue<string>() != "in" || entry["data"] is not JsonArray frame
                || frame.Count < 2 || frame[0]?.GetValue<string>() is not { } ev || !ev.StartsWith("game/", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = ev.Split('/');
            if (parts.Length < 3 || !long.TryParse(parts[1], out long id))
            {
                continue;
            }

            if (!games.TryGetValue(id, out List<string>? list))
            {
                games[id] = list = [];
            }

            list.Add(frame.ToJsonString());
        }

        return games;
    }

    private static async Task ReplayAsync(string file, long gameId, List<string> frames)
    {
        var server = new FakeServer();
        var logs = new CapturingLoggerFactory();
        await using var client = new OgsRealtimeClient(
            new Uri("wss://beta.online-go.com"), server.Factory, () => "jwt",
            new OgsRealtimeOptions { DeviceId = "d", PingInterval = TimeSpan.FromHours(1), PongTimeout = TimeSpan.FromHours(1), ReconnectDelays = [TimeSpan.FromMilliseconds(10)] },
            TimeProvider.System, logs.CreateLogger("replay"));
        var session = new OgsGameSession(client, gameId);
        OgsGameSnapshot? data = null;
        OgsGameResult? result = null;
        var moves = new List<OgsGameMove>();
        session.GamedataReceived += (_, g) => data = g;
        session.MoveReceived += (_, m) => { lock (moves) { moves.Add(m); } };
        session.GameEnded += (_, r) => result = r;
        session.Connect();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await server.NextConnectionAsync();

        int expectedMoves = 0;
        bool ends = false;
        foreach (string frame in frames)
        {
            using JsonDocument doc = JsonDocument.Parse(frame);
            string ev = doc.RootElement[0].GetString()!;
            JsonElement payload = doc.RootElement[1];
            if (ev.EndsWith("/move", StringComparison.Ordinal))
            {
                expectedMoves++;
            }
            else if (ev.EndsWith("/gamedata", StringComparison.Ordinal))
            {
                expectedMoves = 0; // a full snapshot restarts the count of moves after it
                ends |= payload.TryGetProperty("phase", out JsonElement phase) && phase.GetString() == "finished";
            }
            else if (ev.EndsWith("/removed_stones_accepted", StringComparison.Ordinal)
                || (ev.EndsWith("/phase", StringComparison.Ordinal) && payload.ValueKind == JsonValueKind.String && payload.GetString() == "finished"))
            {
                ends = true;
            }

            c.Push(frame);
        }

        await OgsRealtimeClientTests.WaitUntil(() => data is not null && Volatile.Read(ref expectedMoves) <= moves.Count && (!ends || result is not null));
        data.Should().NotBeNull($"{file}: game {gameId} gamedata");
        if (ends)
        {
            result.Should().NotBeNull($"{file}: game {gameId} should end");
        }

        logs.Lines.Should().NotContain(l => l.Contains("Exception", StringComparison.Ordinal), $"{file}: game {gameId} parsed without errors");
    }
}
