using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Core;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Rest;

namespace Hoshi.Ogs.Tests;

public sealed class OgsRankTests
{
    [Theory]
    [InlineData(25.4, false, "5k")]
    [InlineData(29.9, false, "1k")]
    [InlineData(0, false, "30k")]
    [InlineData(30, false, "1d")]
    [InlineData(31.2, false, "2d")]
    [InlineData(37, true, "1p")]
    [InlineData(1045, true, "9p")]
    public void Formats_ranks_like_the_web_client(double ranking, bool pro, string expected)
    {
        OgsRank.Format(ranking, pro).Should().Be(expected);
    }
}

public sealed class OgsRestClientTests
{
    private static (OgsRestClient Client, FakeHttpHandler Http) Create(OgsAuthMode mode = OgsAuthMode.OAuth)
    {
        var http = new FakeHttpHandler();
        var client = new HttpClient(http) { BaseAddress = new Uri("https://online-go.com") };
        var credentials = new FakeCredentials(mode);
        return (new OgsRestClient(client, credentials), http);
    }

    [Fact]
    public async Task Active_games_come_from_the_overview()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("GET", "/api/v1/ui/overview", HttpStatusCode.OK, Fixtures.Read("ui_overview.json"));

        IReadOnlyList<OgsActiveGame> games = await rest.GetActiveGamesAsync(CancellationToken.None);

        games.Should().HaveCount(2);
        OgsActiveGame g = games[0];
        g.Id.Should().Be(5550001);
        g.Name.Should().Be("Friendly Match");
        g.Black.Username.Should().Be("kuro_test");
        g.White.Rank.Should().Be("2d");
        g.Width.Should().Be(19);
        g.Phase.Should().Be("play");
        g.IsTurnOf(1001).Should().BeTrue();
        games[1].IsTurnOf(1001).Should().BeFalse();
        http.Single("GET", "/api/v1/ui/overview").Request.Headers.Authorization!.ToString().Should().Be("Bearer test-access");
    }

    [Fact]
    public async Task Open_challenge_payload_matches_the_web_client()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("POST", "/api/v1/challenges", HttpStatusCode.OK, """{"challenge":123,"game":456,"uuid":"u"}""");

        CreatedChallenge created = await rest.CreateChallengeAsync(new ChallengeRequest
        {
            Name = "Test Game",
            Rules = RuleSet.Japanese,
            TimeControl = TimeControlSettings.ByoYomi(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(30), 5),
        }, opponentId: null, CancellationToken.None);

        created.Should().Be(new CreatedChallenge(123, 456, IsLive: true));
        JsonNode expected = JsonNode.Parse("""
            {
              "initialized": false,
              "challenger_color": "automatic",
              "invite_only": false,
              "min_ranking": -1000,
              "max_ranking": 1000,
              "rengo_auto_start": 0,
              "game": {
                "name": "Test Game",
                "rules": "japanese",
                "ranked": false,
                "width": 19,
                "height": 19,
                "handicap": 0,
                "komi_auto": "automatic",
                "disable_analysis": false,
                "initial_state": null,
                "private": false,
                "time_control": "byoyomi",
                "time_control_parameters": {
                  "system": "byoyomi",
                  "speed": "live",
                  "main_time": 1200,
                  "period_time": 30,
                  "periods": 5,
                  "pause_on_weekends": false,
                  "time_control": "byoyomi"
                },
                "pause_on_weekends": false
              }
            }
            """)!;
        JsonNode.DeepEquals(JsonNode.Parse(http.Single("POST", "/api/v1/challenges").Body!), expected).Should().BeTrue(
            http.Single("POST", "/api/v1/challenges").Body);
    }

    [Fact]
    public async Task Direct_challenge_goes_to_the_player_and_custom_komi_is_sent()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("POST", "/api/v1/players/2002/challenge", HttpStatusCode.OK, """{"challenge":7,"game":{"id":8}}""");

        CreatedChallenge created = await rest.CreateChallengeAsync(new ChallengeRequest
        {
            Name = "Práctica",
            Width = 9,
            Height = 9,
            Rules = RuleSet.Chinese,
            Komi = 7.5,
            Color = ChallengeColor.Black,
            Private = true,
            TimeControl = TimeControlSettings.Fischer(TimeSpan.FromDays(3), TimeSpan.FromDays(1), TimeSpan.FromDays(7)),
        }, opponentId: 2002, CancellationToken.None);

        created.Should().Be(new CreatedChallenge(7, 8, IsLive: false));
        using JsonDocument doc = JsonDocument.Parse(http.Single("POST", "/api/v1/players/2002/challenge").Body!);
        JsonElement game = doc.RootElement.GetProperty("game");
        doc.RootElement.GetProperty("challenger_color").GetString().Should().Be("black");
        game.GetProperty("komi_auto").GetString().Should().Be("custom");
        game.GetProperty("komi").GetDouble().Should().Be(7.5);
        game.GetProperty("ranked").GetBoolean().Should().BeFalse("Hoshi never creates ranked games");
        game.GetProperty("private").GetBoolean().Should().BeTrue();
        game.GetProperty("time_control_parameters").GetProperty("speed").GetString().Should().Be("correspondence");
        game.GetProperty("time_control_parameters").GetProperty("initial_time").GetInt32().Should().Be(259200);
    }

    [Fact]
    public async Task Fischer_and_simple_time_controls()
    {
        TimeControlSettings.Fischer(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).ToJson()
            .ToJsonString().Should().Be("""{"system":"fischer","speed":"live","initial_time":300,"time_increment":10,"max_time":600,"pause_on_weekends":false,"time_control":"fischer"}""");
        TimeControlSettings.Simple(TimeSpan.FromSeconds(5)).Speed.Should().Be("blitz");
        TimeControlSettings.ByoYomi(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), 3).Speed.Should().Be("blitz");
        TimeControlSettings.ByoYomi(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10), 3).Speed.Should().Be("live", "goban rounds 60/126.5 + 10 to 10 s per move");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Cancel_and_accept_use_the_web_client_routes()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("DELETE", "/api/v1/me/challenges/123", HttpStatusCode.NoContent, "")
            .On("POST", "/api/v1/challenges/900001/accept", HttpStatusCode.OK, """{"game":7770001}""");

        await rest.CancelChallengeAsync(123, CancellationToken.None);
        long gameId = await rest.AcceptChallengeAsync(900001, CancellationToken.None);

        gameId.Should().Be(7770001);
        http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Players_are_found_by_username()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("GET", "/api/v1/players?username=shiro_test", HttpStatusCode.OK,
                """{"count":1,"results":[{"id":2002,"username":"shiro_test","ranking":31.2,"professional":false}]}""")
            .On("GET", "/api/v1/players?username=nobody", HttpStatusCode.OK, """{"count":0,"results":[]}""");

        (await rest.FindPlayerAsync("shiro_test", CancellationToken.None)).Should().Be(new OgsUser(2002, "shiro_test", 31.2, false));
        (await rest.FindPlayerAsync("nobody", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Errors_carry_the_status_code()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create();
        http.On("GET", "/api/v1/ui/overview", HttpStatusCode.Unauthorized, """{"detail":"Authentication credentials were not provided."}""");

        OgsApiException ex = (await FluentActions.Awaiting(() => rest.GetActiveGamesAsync(CancellationToken.None))
            .Should().ThrowAsync<OgsApiException>()).Which;

        ex.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Message.Should().Contain("Authentication credentials");
    }

    [Fact]
    public async Task Password_mode_sends_csrf_on_writes()
    {
        (OgsRestClient rest, FakeHttpHandler http) = Create(OgsAuthMode.Password);
        http.On("DELETE", "/api/v1/me/challenges/5", HttpStatusCode.NoContent, "");

        await rest.CancelChallengeAsync(5, CancellationToken.None);

        HttpRequestMessage req = http.Single("DELETE", "/api/v1/me/challenges/5").Request;
        req.Headers.GetValues("X-CSRFToken").Should().Equal("csrf-token");
        req.Headers.Authorization.Should().BeNull();
    }

    private sealed class FakeCredentials(OgsAuthMode mode) : IOgsCredentials
    {
        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (mode == OgsAuthMode.OAuth)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-access");
            }
            else if (request.Method != HttpMethod.Get)
            {
                request.Headers.Add("X-CSRFToken", "csrf-token");
            }

            return Task.CompletedTask;
        }
    }
}
