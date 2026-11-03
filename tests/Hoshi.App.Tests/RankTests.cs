using FluentAssertions;
using Hoshi.App.Services.Engines;
using Hoshi.App.Services.Rank;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.Gtp;

namespace Hoshi.App.Tests;

/// <summary>The estimated rank from games against the human-style KataGo, and the rival at the player's level.</summary>
public sealed class RankTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hoshi-rank").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static EngineChoice Human() =>
        new EngineChoice("KataGo (human)", new GtpEngineConfig("KataGo (human)", "/x/katago", "", ""), IsBuiltIn: true) { FixedId = GtpEngineHost.HumanId };

    [Theory]
    [InlineData("1k", 0)]
    [InlineData("10k", -9)]
    [InlineData("20k", -19)]
    [InlineData("1d", 1)]
    [InlineData("5d", 5)]
    public void Ranks_sit_on_one_line(string rank, double value)
    {
        RankEstimates.Parse(rank).Should().Be(value);
        RankEstimates.Label(value).Should().Be(rank);
    }

    [Fact]
    public void Wins_against_stronger_rivals_move_the_estimate_up_more_and_it_settles()
    {
        var me = new RankEstimate(-9, 0); // 10k
        me.Expected(-8).Should().BeApproximately(0.3, 0.01, "one rank stronger wins about 70 %");

        RankEstimate upset = me.After(-6, 1);
        RankEstimate expected = me.After(-12, 1);
        (upset.Rating - me.Rating).Should().BeGreaterThan(expected.Rating - me.Rating);

        RankEstimate settled = me;
        for (int i = 0; i < 40; i++)
        {
            settled = settled.After(-5, i % 2); // half the games against a 6k
        }

        settled.Rating.Should().BeInRange(-6.5, -3.5, "50 % against a 6k means about 6k");
        settled.Uncertainty.Should().BeLessThan(me.Uncertainty);
    }

    [Theory]
    [InlineData(-9.2, 19, "10k", 0)] // a 10k: the 10k profile, even game
    [InlineData(-12.0, 19, "10k", 3)] // a 13k: the 10k profile with 3 stones
    [InlineData(-4.9, 19, "5k", 0)] // a 6k, even game against the 5k
    [InlineData(-17.0, 9, "15k", 0)] // an 18k on 9×9: 3 ranks are less than one stone there
    [InlineData(-17.0, 19, "15k", 3)] // the same player on 19×19: 3 stones
    [InlineData(8.0, 19, "5d", 0)] // stronger than every profile
    public void The_rival_at_my_level_takes_the_nearest_rank_above_with_handicap_for_the_gap(double rating, int size, string level, int handicap)
    {
        (string l, int h) = RankEstimates.RivalFor(new RankEstimate(rating, 10), GtpEngineHost.HumanLevels, size);
        l.Should().Be(level);
        h.Should().Be(handicap);
    }

    [Fact]
    public void Games_are_kept_on_disk_with_the_handicap_counted()
    {
        var store = new RankStore(_dir);
        store.Current.Should().BeNull();
        store.CurrentOr("15k").Rating.Should().Be(-14);

        RankEstimate after = store.Record("10k", handicap: 4, size: 19, playerWasBlack: true, score: 1, DateTimeOffset.UnixEpoch);

        after.Games.Should().Be(1);
        after.Rating.Should().BeGreaterThan(-14 + 0.1, "beating a 10k with 4 stones is like beating a 14k");
        new RankStore(_dir).Current.Should().Be(after);
        new RankStore(_dir).History.Should().ContainSingle().Which.Handicap.Should().Be(4);
    }

    [Fact]
    public void The_dialog_sets_the_rival_and_handicap_from_the_estimate()
    {
        var options = new NewEngineGameViewModel([Human()], humanLevel: "10k", estimate: new RankEstimate(-12, 8)); // 13k

        options.HasEstimate.Should().BeTrue();
        options.AtMyLevel.Should().BeTrue();
        options.Black.Engine.Should().BeNull("you take Black");
        options.White.Engine!.IsHumanStyle.Should().BeTrue();
        options.HumanLevel.Should().Be("10k");
        options.Handicap.Should().Be(3);
        options.EstimateText.Should().Contain("13k");

        options.Size = 9;
        options.Handicap.Should().Be(0, "on 9×9 three ranks are less than one stone");

        options.AtMyLevel = false;
        options.HumanLevel = "1d";
        options.Size = 19;
        options.HumanLevel.Should().Be("1d", "the player's own choice once 'at my level' is off");
    }
}
