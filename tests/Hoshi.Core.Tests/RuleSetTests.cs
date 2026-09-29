namespace Hoshi.Core.Tests;

public sealed class RuleSetTests
{
    [Theory]
    [InlineData("japanese", ScoringMethod.Territory, KoRule.Simple, false)]
    [InlineData("korean", ScoringMethod.Territory, KoRule.Simple, false)]
    [InlineData("chinese", ScoringMethod.Area, KoRule.PositionalSuperko, false)]
    [InlineData("aga", ScoringMethod.Area, KoRule.SituationalSuperko, false)]
    [InlineData("nz", ScoringMethod.Area, KoRule.SituationalSuperko, true)]
    [InlineData("ing", ScoringMethod.Area, KoRule.PositionalSuperko, true)]
    public void Presets_have_the_expected_properties(string name, ScoringMethod scoring, KoRule ko, bool suicide)
    {
        RuleSet rules = RuleSet.FromName(name);

        rules.Scoring.Should().Be(scoring);
        rules.Ko.Should().Be(ko);
        rules.AllowSuicide.Should().Be(suicide);
        rules.Name.Should().Be(name);
    }

    [Fact]
    public void Names_are_case_insensitive_and_unknown_names_fail()
    {
        RuleSet.FromName("Chinese").Should().BeSameAs(RuleSet.Chinese);
        RuleSet.TryFromName("go-with-dice", out _).Should().BeFalse();
        FluentActions.Invoking(() => RuleSet.FromName("go-with-dice")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_komi_per_rule_set()
    {
        RuleSet.Japanese.DefaultKomi.Should().Be(6.5);
        RuleSet.Chinese.DefaultKomi.Should().Be(7.5);
        RuleSet.NewZealand.DefaultKomi.Should().Be(7);
    }
}
