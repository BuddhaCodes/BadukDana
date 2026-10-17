using Avalonia.Headless.XUnit;
using Hoshi.App.Services;
using Hoshi.App.Services.Export;
using Hoshi.App.Services.Study;
using Hoshi.App.Services.Summary;
using Hoshi.App.Services.Tsumego;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>The month and year summaries, worked out on the computer.</summary>
public sealed class SummaryTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static ReplayEntry Game(string black, string white, string? result, int day, string source = "local") =>
        new(Guid.NewGuid().ToString("N"), "g.sgf", October.AddDays(day), black, white, result, 19, 120, source, null);

    [Fact]
    public void The_player_is_the_name_in_most_games_and_their_results_count()
    {
        ReplayEntry[] games =
        [
            Game("Ana", "Luis", "B+R", 2, "ogs"), // win
            Game("Marta", "Ana", "B+3.5", 5),     // loss
            Game("Ana", "KataGo", "W+T", 9),      // loss
            Game("Ana", "Luis", "B+R", -3),       // September: not counted
            Game("Ana", "Pablo", null, 12),       // no result
        ];

        PlayerSummary s = PlayerSummary.Build(October, October.AddMonths(1), games, [], [], []);
        s.Player.Should().Be("Ana");
        (s.Games, s.OnlineGames, s.Wins, s.Losses).Should().Be((4, 1, 1, 2));

        PlayerSummary named = PlayerSummary.Build(October, October.AddMonths(1), games, [], [], [], me: "Luis");
        (named.Wins, named.Losses).Should().Be((0, 1));
    }

    [Fact]
    public void Accuracy_counts_only_the_players_moves_and_joseki_and_problems_their_dates()
    {
        ReviewRecord[] reviews =
        [
            new("a", October.AddDays(3), October.AddDays(4), new ReviewSide("Ana", 50, 40, 20), new ReviewSide("Luis", 50, 10, 5)),
            new("b", October.AddDays(8), October.AddDays(8), new ReviewSide("Marta", 40, 40, 30), new ReviewSide("Ana", 50, 30, 10)),
            new("c", October.AddDays(-8), October.AddDays(8), new ReviewSide("Ana", 40, 0, 0), new ReviewSide("X", 1, 1, 1)),
        ];
        JosekiCard[] joseki =
        [
            JosekiCard.New("1") with { Box = 4, LastReview = October.AddDays(2) },
            JosekiCard.New("2") with { Box = 1, LastReview = October.AddDays(2) },
            JosekiCard.New("3") with { Box = 5, LastReview = October.AddDays(-2) },
        ];
        TsumegoCard[] problems = [new(2, October, 3, 0, October.AddDays(1)), new(0, October, 0, 2), new(1, October, 1, 0, October.AddDays(-1))];

        PlayerSummary s = PlayerSummary.Build(October, October.AddMonths(1), [], reviews, joseki, problems, me: "ana");
        s.ReviewedGames.Should().Be(2);
        s.Accuracy.Should().BeApproximately(70.0 / 100, 1e-9);
        s.BestMoves.Should().Be(30);
        (s.JosekiReviewed, s.JosekiLearned, s.ProblemsSolved).Should().Be((2, 1, 1));
    }

    [Fact]
    public void Reviews_are_logged_by_colour_and_kept_on_disk()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hoshi-reviews-" + Guid.NewGuid().ToString("N"));
        try
        {
            GameTree tree = GameTree.Create(9, RuleSet.Japanese);
            tree.Info.BlackPlayer = "Ana";
            tree.Info.WhitePlayer = "Luis";
            tree.Info.Date = "2026-10-03,04";
            var cursor = new GameCursor(tree);
            cursor.Play(new Point(2, 2));
            GameNode b1 = cursor.Current;
            cursor.Play(new Point(6, 6));
            GameNode w1 = cursor.Current;
            cursor.Play(new Point(2, 6));
            GameNode b2 = cursor.Current;
            var verdicts = new Dictionary<GameNode, StudyVerdict>
            {
                [b1] = new(MoveQuality.Best, 0, null),
                [w1] = new(MoveQuality.Blunder, 9, new Point(4, 4)),
                [b2] = new(MoveQuality.Inaccuracy, 2, null),
            };

            ReviewRecord record = new ReviewLog(dir).Record(tree, verdicts, October.AddDays(20))!;
            record.Played.Should().Be(new DateTimeOffset(2026, 10, 3, 0, 0, 0, record.Played.Offset));
            record.Black.Should().Be(new ReviewSide("Ana", 2, 1, 1));
            record.White.Should().Be(new ReviewSide("Luis", 1, 0, 0));

            var again = new ReviewLog(dir);
            again.Records.Should().ContainSingle().Which.Should().Be(record);
            again.Record(tree, verdicts, October.AddDays(21));
            again.Records.Should().ContainSingle("the same game replaces its review");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void The_summary_card_shows_only_tiles_with_something_and_can_hide_the_name()
    {
        var s = new PlayerSummary(October, October.AddMonths(1), "Ana", 12, 7, 6, 5, 3, 0.734, 81, 0, 0, 4);
        var tiles = ShareViewModel.SummaryTiles(s);
        tiles.Select(t => t.Number).Should().Contain("12").And.Contain("6–5").And.Contain("81").And.Contain("4");
        tiles.Should().HaveCount(5, "no joseki this month");

        var share = new ShareViewModel(new GameViewModel(), summary: (_, _) => s, time: new FixedTime(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero)));
        share.Period(year: false).From.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        share.Period(year: true).To.Year.Should().Be(2027);
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(share.SummaryPng(year: false, CardFormat.Square)!));
        bitmap.PixelSize.Should().Be(ShareCard.Size(CardFormat.Square));
        new ShareViewModel(new GameViewModel()).SummaryPng(false, CardFormat.Square).Should().BeNull();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
