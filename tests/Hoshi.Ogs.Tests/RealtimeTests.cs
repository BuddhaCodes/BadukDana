using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Tests;

public sealed class OgsRealtimeClientTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly FakeClock _clock = new();
    private readonly CapturingLoggerFactory _logs = new();
    private string _jwt = "jwt-1";
    private OgsRealtimeClient? _client;

    private OgsRealtimeClient Create(OgsRealtimeOptions? options = null)
    {
        _client = new OgsRealtimeClient(
            new Uri("wss://online-go.com"),
            _server.Factory,
            () => _jwt,
            options ?? new OgsRealtimeOptions
            {
                DeviceId = "device-1",
                ClientName = "hoshi",
                ClientVersion = "0.1.0",
                Language = "es",
                PingInterval = TimeSpan.FromHours(1),
                PongTimeout = TimeSpan.FromHours(1),
                ReconnectDelays = [TimeSpan.FromMilliseconds(10)],
            },
            _clock,
            _logs.CreateLogger("rt"));
        return _client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement;

    [Fact]
    public async Task Authenticates_first_after_connecting()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();

        JsonElement auth = Parse(await c.ReadAsync());

        c.Uri.Should().Be(new Uri("wss://online-go.com"));
        auth[0].GetString().Should().Be("authenticate");
        auth[1].GetProperty("jwt").GetString().Should().Be("jwt-1");
        auth[1].GetProperty("device_id").GetString().Should().Be("device-1");
        auth[1].GetProperty("client").GetString().Should().Be("hoshi");
        auth[1].GetProperty("client_version").GetString().Should().Be("0.1.0");
        auth[1].GetProperty("language").GetString().Should().Be("es");
        auth[1].GetProperty("user_agent").GetString().Should().StartWith("Hoshi/0.1.0");
    }

    [Fact]
    public async Task Requests_are_correlated_with_responses_by_id()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync(); // authenticate

        Task<JsonElement> request = client.RequestAsync("hostinfo", new JsonObject(), CancellationToken.None);
        JsonElement sent = Parse(await c.ReadCommandAsync("hostinfo"));
        int id = sent[2].GetInt32();
        c.Push($"[{id},{{\"hostname\":\"gs-1\"}}]");

        (await request).GetProperty("hostname").GetString().Should().Be("gs-1");
    }

    [Fact]
    public async Task Error_responses_fault_the_request()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        Task<JsonElement> request = client.RequestAsync("game/move", new JsonObject { ["game_id"] = 1 }, CancellationToken.None);
        int id = Parse(await c.ReadCommandAsync("game/move"))[2].GetInt32();
        c.Push($"[{id},null,{{\"code\":\"not_your_turn\",\"message\":\"Not your turn\"}}]");

        (await FluentActions.Awaiting(() => request).Should().ThrowAsync<OgsRealtimeException>()).Which.Message.Should().Contain("Not your turn");
    }

    [Fact]
    public async Task Events_reach_subscribers_until_they_unsubscribe()
    {
        OgsRealtimeClient client = Create();
        var received = new List<string>();
        IDisposable sub = client.Subscribe("active_game", e => received.Add(e.GetProperty("name").GetString()!));
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        c.Push("""["active_game",{"id":1,"name":"first"}]""");
        await WaitUntil(() => received.Count == 1);
        sub.Dispose();
        c.Push("""["active_game",{"id":2,"name":"second"}]""");
        c.Push("""["other",{}]""");
        await Task.Delay(50);

        received.Should().Equal("first");
    }

    [Fact]
    public async Task Messages_sent_while_offline_are_queued_until_authenticated()
    {
        OgsRealtimeClient client = Create();
        client.Send("seek_graph/connect", new JsonObject { ["channel"] = "global" });

        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();

        Parse(await c.ReadAsync())[0].GetString().Should().Be("authenticate");
        Parse(await c.ReadAsync())[0].GetString().Should().Be("seek_graph/connect");
    }

    [Fact]
    public async Task Ping_measures_latency_and_clock_drift()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        client.PingNow();
        JsonElement ping = Parse(await c.ReadCommandAsync("net/ping"));
        long sentAt = ping[1].GetProperty("client").GetInt64();
        sentAt.Should().Be(_clock.Now.ToUnixTimeMilliseconds());

        // 100 ms round trip; the server clock is 2 s behind ours.
        _clock.Now += TimeSpan.FromMilliseconds(100);
        long server = sentAt + 50 - 2000;
        c.Push($"[\"net/pong\",{{\"client\":{sentAt},\"server\":{server}}}]");
        await WaitUntil(() => client.LatencyMs > 0);

        client.LatencyMs.Should().Be(100);
        client.ClockDriftMs.Should().Be(2000, "drift = now - latency/2 - server");
        client.ServerNow.ToUnixTimeMilliseconds().Should().Be(_clock.Now.ToUnixTimeMilliseconds() - 2000);
    }

    [Fact]
    public async Task Missing_pong_drops_the_connection_and_reconnects()
    {
        OgsRealtimeClient client = Create(new OgsRealtimeOptions
        {
            DeviceId = "d",
            PingInterval = TimeSpan.FromMilliseconds(30),
            PongTimeout = TimeSpan.FromMilliseconds(30),
            ReconnectDelays = [TimeSpan.FromMilliseconds(10)],
        });
        await client.StartAsync(CancellationToken.None);
        FakeConnection first = await _server.NextConnectionAsync();

        FakeConnection second = await _server.NextConnectionAsync();

        first.ClosedByClient.Should().BeTrue();
        Parse(await second.ReadAsync())[0].GetString().Should().Be("authenticate");
    }

    [Fact]
    public async Task Reconnects_re_authenticates_and_raises_Connected_again()
    {
        OgsRealtimeClient client = Create();
        int connected = 0;
        client.Connected += (_, _) => Interlocked.Increment(ref connected);
        await client.StartAsync(CancellationToken.None);
        FakeConnection first = await _server.NextConnectionAsync();
        await first.ReadAsync();

        _jwt = "jwt-2";
        first.ServerClose(1006);
        FakeConnection second = await _server.NextConnectionAsync();

        Parse(await second.ReadAsync())[1].GetProperty("jwt").GetString().Should().Be("jwt-2");
        await WaitUntil(() => connected == 2);
        client.State.Should().Be(OgsConnectionState.Connected);
    }

    [Fact]
    public async Task Pending_requests_fail_when_the_connection_drops()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        Task<JsonElement> request = client.RequestAsync("hostinfo", null, CancellationToken.None);
        await c.ReadCommandAsync("hostinfo");
        c.ServerClose(1006);

        await FluentActions.Awaiting(() => request).Should().ThrowAsync<OgsRealtimeException>();
    }

    [Fact]
    public async Task Connection_failures_are_retried()
    {
        _server.FailNextConnects = 2;
        OgsRealtimeClient client = Create();

        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();

        _server.All.Should().HaveCount(3);
        Parse(await c.ReadAsync())[0].GetString().Should().Be("authenticate");
    }

    [Theory]
    [InlineData(1014)]
    [InlineData(1015)]
    public async Task Unrecoverable_close_codes_stop_reconnecting(int code)
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        c.ServerClose(code);
        await WaitUntil(() => client.State == OgsConnectionState.Failed);
        await Task.Delay(50);

        _server.All.Should().HaveCount(1);
    }

    [Fact]
    public async Task Unparseable_frames_close_with_4000_and_reconnect()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection first = await _server.NextConnectionAsync();
        await first.ReadAsync();

        first.Push("{not json");
        FakeConnection second = await _server.NextConnectionAsync();

        first.CloseStatus.Should().Be(4000);
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task Server_pushed_jwt_is_used_on_the_next_authentication()
    {
        OgsRealtimeClient client = Create();
        string? pushed = null;
        client.JwtUpdated += (_, jwt) => pushed = jwt;
        await client.StartAsync(CancellationToken.None);
        FakeConnection first = await _server.NextConnectionAsync();
        await first.ReadAsync();

        first.Push("""["user/jwt","jwt-rotated"]""");
        await WaitUntil(() => pushed is not null);
        first.ServerClose(1006);
        FakeConnection second = await _server.NextConnectionAsync();

        pushed.Should().Be("jwt-rotated");
        Parse(await second.ReadAsync())[1].GetProperty("jwt").GetString().Should().Be("jwt-rotated");
    }

    [Fact]
    public async Task Stopping_closes_cleanly_without_reconnecting()
    {
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();

        await client.StopAsync();

        c.CloseStatus.Should().Be(1000);
        client.State.Should().Be(OgsConnectionState.Disconnected);
        await Task.Delay(50);
        _server.All.Should().HaveCount(1);
    }

    [Fact]
    public async Task The_jwt_is_never_logged()
    {
        _jwt = "super-secret-jwt";
        OgsRealtimeClient client = Create();
        await client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        await c.ReadAsync();
        c.ServerClose(1006);
        await _server.NextConnectionAsync();

        _logs.Lines.Should().NotBeEmpty();
        _logs.Lines.Should().NotContain(l => l.Contains("super-secret-jwt"));
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(5);
        }
    }
}
