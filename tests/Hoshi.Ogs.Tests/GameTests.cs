using System.Text.Json;
using Hoshi.Core;
using Hoshi.Ogs.Games;
using Hoshi.Ogs.Realtime;

namespace Hoshi.Ogs.Tests;

public sealed class OgsGameParserTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parses_gamedata()
    {
        OgsGameSnapshot g = OgsGameParser.ParseGamedata(Json(Fixtures.Read("gamedata_9x9.json")));

        g.GameId.Should().Be(70000001);
        g.Width.Should().Be(9);
        g.Rules.Should().BeSameAs(RuleSet.Japanese);
        g.Komi.Should().Be(6.5);
        g.Black.Should().Be(new OgsUser(1001, "kuro_test", 25.4, false));
        g.White.Username.Should().Be("shiro_test");
        g.Moves.Should().Equal(
            new OgsGameMove(1, Stone.Black, new Point(4, 4)),
            new OgsGameMove(2, Stone.White, new Point(2, 2)),
            new OgsGameMove(3, Stone.Black, null));
        g.Phase.Should().Be(OgsGamePhase.Play);
        g.TimeControl.System.Should().Be("byoyomi");
        g.TimeControl.MainTime.Should().Be(600);
        g.TimeControl.Periods.Should().Be(5);
        g.Clock!.CurrentColor.Should().Be(Stone.White);
        g.Clock.White!.ThinkingTime.Should().Be(596.9);
        g.ColorOf(2002).Should().Be(Stone.White);
        g.ColorOf(3).Should().Be(Stone.Empty);
        g.Result.Should().BeNull();
    }

    [Fact]
    public void Fixed_handicap_comes_as_initial_state_and_white_moves_first()
    {
        OgsGameSnapshot g = OgsGameParser.ParseGamedata(Json("""
            {"game_id":5,"width":19,"height":19,"handicap":2,"initial_player":"white",
             "initial_state":{"black":"pddp","white":""},"black_player_id":1,"white_player_id":2,
             "moves":[[16,16,100],[2,2,100]]}
            """));

        g.InitialBlack.Should().Equal(new Point(15, 3), new Point(3, 15));
        g.Moves.Select(m => m.Color).Should().Equal(Stone.White, Stone.Black);
    }

    [Fact]
    public void Free_handicap_placement_gives_black_the_first_moves()
    {
        OgsGameSnapshot g = OgsGameParser.ParseGamedata(Json("""
            {"game_id":5,"width":19,"height":19,"handicap":3,"free_handicap_placement":true,
             "black_player_id":1,"white_player_id":2,
             "moves":[[3,3],[15,15],[3,15],[15,3],[9,9]]}
            """));

        g.Moves.Select(m => m.Color).Should().Equal(Stone.Black, Stone.Black, Stone.Black, Stone.White, Stone.Black);
        g.ColorForMove(5).Should().Be(Stone.White);
    }

    [Fact]
    public void Finished_games_carry_their_result()
    {
        OgsGameSnapshot g = OgsGameParser.ParseGamedata(Json("""
            {"game_id":5,"width":9,"height":9,"black_player_id":1,"white_player_id":2,
             "phase":"finished","winner":2,"outcome":"Resignation","moves":[]}
            """));

        g.Phase.Should().Be(OgsGamePhase.Finished);
        g.Result.Should().Be(new OgsGameResult(Stone.White, "Resignation"));
        g.Result!.ToSgf().Should().Be("W+R");
        g.Result.Describe().Should().Be("Ganan blancas por abandono");
    }

    [Theory]
    [InlineData("black", "3.5 points", "B+3.5", "Ganan negras por 3.5 puntos")]
    [InlineData("white", "Timeout", "W+T", "Ganan blancas por tiempo")]
    [InlineData("white", "Disqualification", "W+F", "Ganan blancas (Disqualification)")]
    public void Results_map_to_SGF(string winner, string outcome, string sgf, string text)
    {
        var r = new OgsGameResult(winner == "black" ? Stone.Black : Stone.White, outcome);

        r.ToSgf().Should().Be(sgf);
        r.Describe().Should().Be(text);
    }

    [Fact]
    public void Moves_accept_packed_arrays_objects_and_passes()
    {
        OgsGameParser.TryParseMove(Json("[3, 4, 1200]"), out Point? a, out _).Should().BeTrue();
        a.Should().Be(new Point(3, 4));
        OgsGameParser.TryParseMove(Json("[-1, -1]"), out Point? pass, out _).Should().BeTrue();
        pass.Should().BeNull();
        OgsGameParser.TryParseMove(Json("""{"x":0,"y":8,"color":2}"""), out Point? o, out Stone c).Should().BeTrue();
        o.Should().Be(new Point(0, 8));
        c.Should().Be(Stone.White);
        OgsGameParser.TryParseMove(Json("\"dd\""), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Chat_lines_are_read()
    {
        OgsChatLine? line = OgsGameParser.ParseChat(Json("""
            {"channel":"main","line":{"chat_id":"abc","body":"hola","date":1790000100,"move_number":3,"player_id":2002,"username":"shiro_test"}}
            """));

        line.Should().Be(new OgsChatLine("abc", "main", 2002, "shiro_test", "hola", 3, DateTimeOffset.FromUnixTimeSeconds(1790000100)));
    }
}

public sealed class OgsClockMathTests
{
    private static readonly OgsTimeControl ByoYomi = new() { System = "byoyomi", MainTime = 600, PeriodTime = 30, Periods = 5 };
    private static readonly OgsTimeControl Fischer = new() { System = "fischer", MainTime = 300, Increment = 10, MaxTime = 600 };

    private static OgsClock Clock(OgsPlayerClockState black, OgsPlayerClockState white, long current = 1, long? pausedSince = null, bool paused = false) =>
        new(1, current, 1, 2, LastMoveMs: 1_000_000, ExpirationMs: 0, black, white, pausedSince, paused);

    private static DateTimeOffset At(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    [Fact]
    public void Only_the_player_to_move_loses_time()
    {
        OgsClock clock = Clock(new(100, 5, 30), new(200, 5, 30));

        (OgsClockReading b, OgsClockReading w) = OgsClockMath.Read(clock, ByoYomi, At(1_000_000 + 40_000));

        b.Main.Should().Be(TimeSpan.FromSeconds(60));
        w.Main.Should().Be(TimeSpan.FromSeconds(200));
        b.Format().Should().Be("1:00 + 5×0:30");
    }

    [Fact]
    public void Byo_yomi_consumes_whole_periods_after_main_time()
    {
        OgsClock clock = Clock(new(10, 5, 30), new(0, 5, 30));

        // 10 s main + 65 s overtime = two full periods used, 25 s left in the third.
        OgsClockReading b = OgsClockMath.Read(clock, ByoYomi, At(1_000_000 + 75_000)).Black;

        b.Main.Should().Be(TimeSpan.Zero);
        b.PeriodsLeft.Should().Be(3);
        b.PeriodLeft.Should().Be(TimeSpan.FromSeconds(25));
        b.TimedOut.Should().BeFalse();
        b.Format().Should().Be("0:25 (3)");
    }

    [Fact]
    public void Byo_yomi_times_out_when_the_last_period_is_used()
    {
        OgsClockReading b = OgsClockMath.Read(Clock(new(0, 1, 30), new(0, 1, 30)), ByoYomi, At(1_000_000 + 31_000)).Black;

        b.PeriodsLeft.Should().Be(0);
        b.TimedOut.Should().BeTrue();
    }

    [Fact]
    public void Fischer_counts_down_and_stops_at_zero()
    {
        OgsClock clock = Clock(new(12.5), new(300), current: 2);

        (OgsClockReading b, OgsClockReading w) = OgsClockMath.Read(clock, Fischer, At(1_000_000 + 400_000));

        b.Main.Should().Be(TimeSpan.FromSeconds(12.5));
        b.Format().Should().Be("0:13");
        w.Main.Should().Be(TimeSpan.Zero);
        w.TimedOut.Should().BeTrue();
    }

    [Fact]
    public void A_paused_clock_stops_at_paused_since()
    {
        OgsClock clock = Clock(new(100), new(100), pausedSince: 1_000_000 + 5_000, paused: true);

        OgsClockMath.Read(clock, Fischer, At(1_000_000 + 60_000)).Black.Main.Should().Be(TimeSpan.FromSeconds(95));
    }

    [Fact]
    public void Nothing_runs_in_start_mode()
    {
        OgsClock clock = Clock(new(100), new(100)) with { StartMode = true };

        OgsClockMath.Read(clock, Fischer, At(1_000_000 + 60_000)).Black.Main.Should().Be(TimeSpan.FromSeconds(100));
    }

    [Theory]
    [InlineData(59.2, "1:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(90000, "1 d 1 h")]
    [InlineData(0, "0:00")]
    public void Formats_as_a_countdown(double seconds, string text)
    {
        new OgsClockReading(TimeSpan.FromSeconds(seconds)).Format().Should().Be(text);
    }
}

public sealed class OgsGameSessionTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly OgsRealtimeClient _client;

    public OgsGameSessionTests()
    {
        _client = new OgsRealtimeClient(
            new Uri("wss://online-go.com"), _server.Factory, () => "jwt",
            new OgsRealtimeOptions { DeviceId = "d", PingInterval = TimeSpan.FromHours(1), PongTimeout = TimeSpan.FromHours(1), ReconnectDelays = [TimeSpan.FromMilliseconds(10)] },
            TimeProvider.System, null);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task<(OgsGameSession Session, FakeConnection Connection)> ConnectAsync()
    {
        var session = new OgsGameSession(_client, 70000001);
        session.Connect();
        await _client.StartAsync(CancellationToken.None);
        FakeConnection c = await _server.NextConnectionAsync();
        string connect = await c.ReadCommandAsync("game/connect");
        JsonDocument.Parse(connect).RootElement[1].GetProperty("game_id").GetInt64().Should().Be(70000001);
        return (session, c);
    }

    private static void Push(FakeConnection c, string ev, string json) => c.Push($"[\"game/70000001/{ev}\",{json}]");

    [Fact]
    public async Task Gamedata_then_moves_are_raised_in_order_with_their_colours()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();
        var moves = new List<OgsGameMove>();
        OgsGameSnapshot? data = null;
        session.GamedataReceived += (_, g) => data = g;
        session.MoveReceived += (_, m) => { lock (moves) { moves.Add(m); } };

        Push(c, "gamedata", Fixtures.Read("gamedata_9x9.json"));
        await OgsRealtimeClientTests.WaitUntil(() => data is not null);
        Push(c, "move", """{"game_id":70000001,"move_number":3,"move":[6,2,5000]}""");
        Push(c, "move", """{"game_id":70000001,"move_number":4,"move":[-1,-1,900]}""");
        await OgsRealtimeClientTests.WaitUntil(() => moves.Count == 2);

        moves.Should().Equal(new OgsGameMove(4, Stone.White, new Point(6, 2)), new OgsGameMove(5, Stone.Black, null));
        session.MoveCount.Should().Be(5);
        session.Snapshot!.Moves.Should().HaveCount(5);
    }

    [Fact]
    public async Task Commands_are_sent_in_OGS_format()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();

        session.Play(new Point(3, 2));
        (await c.ReadCommandAsync("game/move")).Should().Contain("\"move\":\"dc\"").And.Contain("\"game_id\":70000001");
        session.Play(null);
        (await c.ReadCommandAsync("game/move")).Should().Contain("\"move\":\"..\"");
        session.SendChat(" hola ");
        (await c.ReadCommandAsync("game/chat")).Should().Contain("\"body\":\"hola\"").And.Contain("\"type\":\"main\"");
        session.SetRemovedStones([new Point(0, 0), new Point(1, 0)], removed: true);
        (await c.ReadCommandAsync("game/removed_stones/set")).Should().Contain("\"stones\":\"aaba\"").And.Contain("\"removed\":true");
        session.AcceptRemovedStones([new Point(1, 0), new Point(0, 0)]);
        (await c.ReadCommandAsync("game/removed_stones/accept")).Should().Contain("\"stones\":\"aaba\"").And.Contain("\"strict_seki_mode\":false");
        session.Resign();
        (await c.ReadCommandAsync("game/resign")).Should().Contain("70000001");
    }

    [Fact]
    public async Task Stone_removal_and_scoring_end_the_game()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();
        var phases = new List<OgsGamePhase>();
        IReadOnlyList<Point>? removed = null;
        OgsGameResult? result = null;
        session.PhaseChanged += (_, p) => { lock (phases) { phases.Add(p); } };
        session.RemovedStonesChanged += (_, r) => removed = r;
        session.GameEnded += (_, r) => result = r;
        Push(c, "gamedata", Fixtures.Read("gamedata_9x9.json"));

        Push(c, "phase", "\"stone removal\"");
        Push(c, "removed_stones", """{"removed":true,"stones":"aa","all_removed":"aaba"}""");
        Push(c, "removed_stones_accepted", Fixtures.Read("removed_stones_accepted.json"));
        await OgsRealtimeClientTests.WaitUntil(() => result is not null);

        phases.Should().Equal(OgsGamePhase.StoneRemoval, OgsGamePhase.Finished);
        removed.Should().Equal(new Point(0, 0), new Point(1, 0));
        result.Should().Be(new OgsGameResult(Stone.Black, "3.5 points", 30, 26.5));
        session.Snapshot!.Phase.Should().Be(OgsGamePhase.Finished);
    }

    [Fact]
    public async Task Resignation_arrives_as_finished_gamedata()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();
        OgsGameResult? result = null;
        session.GameEnded += (_, r) => result = r;

        Push(c, "gamedata", Fixtures.Read("gamedata_9x9.json").Replace("\"phase\": \"play\"", "\"phase\": \"finished\", \"winner\": 2002, \"outcome\": \"Resignation\"", StringComparison.Ordinal));
        await OgsRealtimeClientTests.WaitUntil(() => result is not null);

        result!.ToSgf().Should().Be("W+R");
    }

    [Fact]
    public async Task Clock_chat_errors_and_undo_are_forwarded()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();
        OgsClock? clock = null;
        OgsChatLine? chat = null;
        string? error = null;
        int undoTo = -1;
        session.ClockChanged += (_, k) => clock = k;
        session.ChatReceived += (_, l) => chat = l;
        session.ErrorReceived += (_, e) => error = e;
        session.UndoAccepted += (_, n) => undoTo = n;
        Push(c, "gamedata", Fixtures.Read("gamedata_9x9.json"));

        Push(c, "clock", """{"game_id":70000001,"current_player":1001,"black_player_id":1001,"white_player_id":2002,"last_move":1,"expiration":2,"black_time":{"thinking_time":500,"periods":5,"period_time":30},"white_time":{"thinking_time":400,"periods":4,"period_time":30}}""");
        Push(c, "chat", """{"channel":"main","line":{"chat_id":"x","body":"gg","date":1790000100,"move_number":3,"player_id":2002,"username":"shiro_test"}}""");
        Push(c, "error", "\"Illegal move\"");
        Push(c, "undo_accepted", "2");
        await OgsRealtimeClientTests.WaitUntil(() => undoTo == 2);

        clock!.CurrentColor.Should().Be(Stone.Black);
        clock.White!.Periods.Should().Be(4);
        chat!.Body.Should().Be("gg");
        error.Should().Be("Illegal move");
        session.MoveCount.Should().Be(2);
    }

    [Fact]
    public async Task Reconnecting_rejoins_the_game_and_dispose_leaves_it()
    {
        (OgsGameSession session, FakeConnection c) = await ConnectAsync();

        c.ServerClose(1006);
        FakeConnection again = await _server.NextConnectionAsync();
        await again.ReadCommandAsync("game/connect");

        session.Dispose();
        (await again.ReadCommandAsync("game/disconnect")).Should().Contain("70000001");
    }
}
