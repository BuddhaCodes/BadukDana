using System.Text.Json;
using Hoshi.Core;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Tests;

public sealed class OgsSeekGraphTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly OgsRealtimeClient _client;

    public OgsSeekGraphTests()
    {
        _client = new OgsRealtimeClient(
            new Uri("wss://online-go.com"), _server.Factory, () => "jwt",
            new OgsRealtimeOptions { DeviceId = "d", PingInterval = TimeSpan.FromHours(1), PongTimeout = TimeSpan.FromHours(1), ReconnectDelays = [TimeSpan.FromMilliseconds(10)] },
            TimeProvider.System, null);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    [Fact]
    public async Task Subscribes_on_every_connection_and_tracks_open_challenges()
    {
        using var graph = new OgsSeekGraph(_client);
        int changes = 0;
        graph.Changed += (_, _) => Interlocked.Increment(ref changes);
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();

        JsonElement connect = JsonDocument.Parse(await c.ReadCommandAsync("seek_graph/connect")).RootElement;
        connect[1].GetProperty("channel").GetString().Should().Be("global");

        c.Push($"[\"seekgraph/global\",{Fixtures.Read("seekgraph_initial.json")}]");
        await OgsRealtimeClientTests.WaitUntil(() => graph.Challenges.Count == 2);

        OgsOpenChallenge open = graph.Challenges.Single(x => x.ChallengeId == 900001);
        open.Challenger.Should().Be(new OgsUser(4004, "open_player", 18.0, false));
        open.Name.Should().Be("Open game");
        open.Width.Should().Be(19);
        open.Ranked.Should().BeFalse();
        open.Rules.Should().BeSameAs(RuleSet.Japanese);
        open.TimeControlSummary.Should().Be("byoyomi 10:00 + 5×30 s");
        open.Speed.Should().Be("live");

        OgsOpenChallenge ranked = graph.Challenges.Single(x => x.ChallengeId == 900002);
        ranked.Ranked.Should().BeTrue();
        ranked.Komi.Should().Be(5.5);
        ranked.TimeControlSummary.Should().Be("fischer 1:00 + 5 s (máx. 2:00)");

        c.Push($"[\"seekgraph/global\",{Fixtures.Read("seekgraph_update.json")}]");
        await OgsRealtimeClientTests.WaitUntil(() => graph.Challenges.Count == 0);
        changes.Should().BeGreaterThanOrEqualTo(2);

        c.ServerClose(1006);
        FakeConnection again = await _server.NextConnectionAsync();
        await again.ReadCommandAsync("seek_graph/connect");
    }

    [Fact]
    public async Task Disposing_unsubscribes()
    {
        var graph = new OgsSeekGraph(_client);
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadCommandAsync("seek_graph/connect");

        graph.Dispose();

        (await c.ReadCommandAsync("seek_graph/disconnect")).Should().Contain("global");
    }
}

public sealed class ChallengeKeepAliveTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly OgsRealtimeClient _client;

    public ChallengeKeepAliveTests()
    {
        _client = new OgsRealtimeClient(
            new Uri("wss://online-go.com"), _server.Factory, () => "jwt",
            new OgsRealtimeOptions { DeviceId = "d", PingInterval = TimeSpan.FromHours(1), PongTimeout = TimeSpan.FromHours(1) },
            TimeProvider.System, null);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    [Fact]
    public async Task Sends_keepalives_until_the_game_starts()
    {
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        Task<bool> wait = ChallengeKeepAlive.WaitForOpponentAsync(_client, challengeId: 123, gameId: 456, TimeSpan.FromMilliseconds(20), CancellationToken.None);

        (await c.ReadCommandAsync("game/connect")).Should().Contain("456");
        JsonElement keepalive = JsonDocument.Parse(await c.ReadCommandAsync("challenge/keepalive")).RootElement;
        keepalive[1].GetProperty("challenge_id").GetInt64().Should().Be(123);
        keepalive[1].GetProperty("game_id").GetInt64().Should().Be(456);
        await c.ReadCommandAsync("challenge/keepalive");

        c.Push("""["game/456/gamedata",{"game_id":456,"phase":"play"}]""");

        (await wait).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_stops_waiting_and_disconnects_from_the_game()
    {
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();
        using var cts = new CancellationTokenSource();

        Task<bool> wait = ChallengeKeepAlive.WaitForOpponentAsync(_client, 1, 2, TimeSpan.FromMilliseconds(20), cts.Token);
        await c.ReadCommandAsync("challenge/keepalive");
        cts.Cancel();

        (await wait).Should().BeFalse();
        (await c.ReadCommandAsync("game/disconnect")).Should().Contain("\"game_id\":2");
    }
}
