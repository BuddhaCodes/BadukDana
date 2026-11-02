using System.Text.Json;
using FluentAssertions;
using Hoshi.Core;
using Hoshi.Ogs.Reviews;

namespace Hoshi.Ogs.Tests;

/// <summary>Review rooms, against the shapes of goban's ReviewMessage (src/engine/GobanEngine.ts).</summary>
public sealed class ReviewTests
{
    [Fact]
    public void Review_log_entries_are_read_as_goban_writes_them()
    {
        const string log = """
            [
              {"ts": 1730000000000, "owner": {"id": 5, "username": "teacher"}, "controller": {"id": 5, "username": "teacher"},
               "gamedata": {"game_id": 4242, "width": 9, "height": 9, "moves": []}},
              {"ts": 1730000001000, "f": 12, "m": "dd..!1ee", "t": "The key point", "k": {"circle": "ccdd", "A": "ee", "triangle": ""}},
              {"ts": 1730000002000, "t+": " (really)"},
              {"ts": 1730000003000, "controller": 77},
              {"chat": {"chat_id": "x", "player_id": 9, "channel": "discussion", "date": 1, "from": 3, "moves": "", "body": "thanks!"}}
            ]
            """;
        OgsReviewMessage[] entries = [.. JsonDocument.Parse(log).RootElement.EnumerateArray().Select(OgsReviewSession.Parse)];

        entries[0].GameId.Should().Be(4242);
        entries[0].OwnerId.Should().Be(5);
        entries[0].ControllerId.Should().Be(5);
        entries[1].From.Should().Be(12);
        entries[1].Moves.Should().Equal(new Point(3, 3), null);
        entries[1].Text.Should().Be("The key point");
        entries[1].Marks!["circle"].Should().Equal(new Point(2, 2), new Point(3, 3));
        entries[1].Marks!["A"].Should().Equal(new Point(4, 4));
        entries[1].Marks!["triangle"].Should().BeEmpty();
        entries[2].TextAppend.Should().Be(" (really)");
        entries[2].Moves.Should().BeNull("an entry without m does not move");
        entries[3].ControllerId.Should().Be(77);
        entries[4].ChatBody.Should().Be("thanks!");
        entries[4].ChatPlayerId.Should().Be(9);
    }

    [Theory]
    [InlineData("https://online-go.com/review/1166030", 1166030L)]
    [InlineData("1166030", 1166030L)]
    [InlineData("https://beta.online-go.com/review/42", 42L)]
    [InlineData("https://online-go.com/game/1166030", null)]
    public void Review_links_are_recognised(string text, long? id)
    {
        OgsLinks.TryReviewId(text).Should().Be(id);
        if (id is { } r)
        {
            OgsLinks.ReviewUri(new Uri("https://online-go.com"), r).ToString().Should().Be($"https://online-go.com/review/{r}");
        }
    }
}
