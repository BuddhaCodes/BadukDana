using System.Text.Json;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Tests;

public sealed class OgsAutomatchTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly OgsRealtimeClient _client;

    public OgsAutomatchTests()
    {
        _client = new OgsRealtimeClient(
            new Uri("wss://online-go.com"), _server.Factory, () => "jwt",
            new OgsRealtimeOptions { DeviceId = "d", PingInterval = TimeSpan.FromHours(1), PongTimeout = TimeSpan.FromHours(1), ReconnectDelays = [TimeSpan.FromMilliseconds(10)] },
            TimeProvider.System, null);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    [Fact]
    public async Task Find_match_sends_the_preferences_and_reports_the_started_game()
    {
        using var automatch = new OgsAutomatch(_client);
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        (long Game, string Uuid)? started = null;
        automatch.Started += (_, s) => started = (s.GameId, s.Uuid);

        string uuid = automatch.Find(new OgsAutomatchRequest(19, "live", "byoyomi", "japanese", AllowHandicap: false, RankRange: 3));
        JsonElement find = JsonDocument.Parse(await c.ReadCommandAsync("automatch/find_match")).RootElement[1];
        find.GetProperty("uuid").GetString().Should().Be(uuid);
        JsonElement option = find.GetProperty("size_speed_options")[0];
        option.GetProperty("size").GetString().Should().Be("19x19");
        option.GetProperty("speed").GetString().Should().Be("live");
        option.GetProperty("system").GetString().Should().Be("byoyomi");
        find.GetProperty("lower_rank_diff").GetInt32().Should().Be(3);
        find.GetProperty("upper_rank_diff").GetInt32().Should().Be(3);
        find.GetProperty("rules").GetProperty("value").GetString().Should().Be("japanese");
        find.GetProperty("rules").GetProperty("condition").GetString().Should().Be("preferred");
        find.GetProperty("handicap").GetProperty("value").GetString().Should().Be("disabled");
        automatch.Searching.Should().Contain(uuid);

        c.Push($"[\"automatch/start\",{{\"uuid\":\"{uuid}\",\"game_id\":70099}}]");
        await OgsRealtimeClientTests.WaitUntil(() => started is not null);
        started.Should().Be((70099L, uuid));
        automatch.Searching.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_is_sent_and_a_server_cancel_clears_the_search()
    {
        using var automatch = new OgsAutomatch(_client);
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        string uuid = automatch.Find(new OgsAutomatchRequest(9, "blitz", "fischer", "chinese", AllowHandicap: true, RankRange: 5));
        await c.ReadCommandAsync("automatch/find_match");

        automatch.Cancel(uuid);
        (await c.ReadCommandAsync("automatch/cancel")).Should().Contain(uuid);
        automatch.Searching.Should().BeEmpty();

        string other = automatch.Find(new OgsAutomatchRequest(9, "blitz", "fischer", "chinese", AllowHandicap: true, RankRange: 5));
        await c.ReadCommandAsync("automatch/find_match");
        c.Push("[\"automatch/cancel\",{\"uuid\":null}]");
        await OgsRealtimeClientTests.WaitUntil(() => automatch.Searching.Count == 0);
        other.Should().NotBe(uuid);
    }

    [Fact]
    public async Task Active_game_updates_tell_whose_turn_it_is()
    {
        using var watcher = new OgsActiveGames(_client);
        OgsGameTurn? turn = null;
        watcher.Updated += (_, t) => turn = t;
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();

        c.Push($"[\"active_game\",{Fixtures.Read("active_game_my_turn.json")}]");
        await OgsRealtimeClientTests.WaitUntil(() => turn is not null);
        turn!.GameId.Should().Be(70012345);
        turn.PlayerToMove.Should().Be(4001);
        turn.Phase.Should().Be("play");
        turn.Black.Should().Be("hoshi-dev");
        turn.White.Should().Be("rival");
        turn.Name.Should().Be("Friendly match");
        turn.IsCorrespondence.Should().BeTrue("a day per move");
    }
}
