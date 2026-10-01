using System.Net;
using Hoshi.Core;
using Hoshi.Ogs.Joseki;

namespace Hoshi.Ogs.Tests;

public sealed class JosekiExplorerTests
{
    private static Point H(string human) => Point.FromHuman(human, 19);

    private static (OgsJosekiClient Client, FakeHttpHandler Http) Client(string? cache = null)
    {
        var http = new FakeHttpHandler()
            .On("GET", "/oje/position?id=root&mode=0", HttpStatusCode.OK, Fixtures.Read("oje_root.json"))
            .On("GET", "/oje/position?id=10&mode=0", HttpStatusCode.OK, Fixtures.Read("oje_q16.json"));
        return (new OgsJosekiClient(new HttpClient(http), cacheDirectory: cache), http);
    }

    [Fact]
    public void Parses_a_position_with_its_rated_next_moves()
    {
        JosekiPosition p = OgsJosekiClient.Parse(Fixtures.Read("oje_q16.json"));

        p.NodeId.Should().Be("10");
        p.Placement.Should().Be(H("Q16"));
        p.IsRoot.Should().BeFalse();
        p.Category.Should().Be(JosekiCategory.Ideal);
        p.Description.Should().Be("The 4-4 point. White can approach at A or invade at the 3-3 point, see #20.");
        p.Tags.Should().Equal("Current");
        p.NextMoves.Select(m => m.Point).Should().Equal(H("R17"), H("R14"), H("O17"), H("Q17"), H("S18"));
        p.NextMoves.Select(m => m.Category).Should().Equal(JosekiCategory.Ideal, JosekiCategory.Ideal, JosekiCategory.Good, JosekiCategory.Mistake, JosekiCategory.Trick);
        p.NextMoves.Count(m => m.IsRecommended).Should().Be(3);
    }

    [Fact]
    public void The_root_has_tenuki_as_a_pass()
    {
        JosekiPosition root = OgsJosekiClient.Parse(Fixtures.Read("oje_root.json"));

        root.IsRoot.Should().BeTrue();
        root.Placement.Should().BeNull();
        root.NextMoves.Should().Contain(m => m.IsTenuki && m.Category == JosekiCategory.Question);
    }

    [Fact]
    public async Task Follows_a_sequence_and_fetches_each_position_once()
    {
        (OgsJosekiClient client, FakeHttpHandler http) = Client();
        var explorer = new JosekiExplorer(client);

        JosekiPosition? p = await explorer.FollowAsync([H("Q16")], CancellationToken.None);
        p!.NodeId.Should().Be("10");
        (await explorer.FollowAsync([H("Q16")], CancellationToken.None))!.NodeId.Should().Be("10");
        (await explorer.FollowAsync([H("D4")], CancellationToken.None)).Should().BeNull("not in the explorer's tree");
        (await explorer.CornerAsync(CancellationToken.None)).Should().Be(Corner.TopRight);

        http.Requests.Should().HaveCount(2, "positions are cached");
        http.Requests[0].Request.Headers.Authorization.Should().BeNull("the explorer is read anonymously");
    }

    [Fact]
    public async Task Positions_are_cached_on_disk()
    {
        string dir = Directory.CreateTempSubdirectory("hoshi-oje").FullName;
        try
        {
            (OgsJosekiClient first, _) = Client(dir);
            await first.GetPositionAsync("10", CancellationToken.None);

            var offline = new FakeHttpHandler();
            var second = new OgsJosekiClient(new HttpClient(offline), cacheDirectory: dir);
            (await second.GetPositionAsync("10", CancellationToken.None)).Placement.Should().Be(H("Q16"));
            offline.Requests.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Errors_surface_as_http_exceptions()
    {
        var client = new OgsJosekiClient(new HttpClient(new FakeHttpHandler()));
        await FluentActions.Invoking(() => client.GetPositionAsync("99", CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
    }
}
