using System.Text.RegularExpressions;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Tests;

public sealed partial class EngineStringsTests
{
    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void Engine_and_OGS_texts_exist_in_both_languages_with_the_same_placeholders()
    {
        string[] keys = [.. Tr.Keys.Where(k => k.StartsWith("Engine.", StringComparison.Ordinal) || k.StartsWith("Ogs.", StringComparison.Ordinal))];
        keys.Should().NotBeEmpty();
        foreach (string key in keys)
        {
            string en = Tr.T(key, Tr.English);
            string es = Tr.T(key, Tr.Spanish);
            en.Should().NotBeNullOrWhiteSpace(key);
            es.Should().NotBeNullOrWhiteSpace(key);
            Placeholders(en).Should().Equal(Placeholders(es), key);
        }
    }

    [Fact]
    public void GPU_tuning_progress_is_recognised_in_English()
    {
        string? a = KataGoProcess.ActivityFrom("Initializing neural net buffer to be size 19 * 19", null, Tr.English);
        a.Should().Be(Tr.T("Engine.LoadingNetwork", Tr.English));
        a = KataGoProcess.ActivityFrom("Performing autotuning", a, Tr.English);
        a.Should().StartWith(Tr.T("Engine.Tuning", Tr.English));
        a = KataGoProcess.ActivityFrom("Tuning 40/69 ...", a, Tr.English);
        a.Should().EndWith("(step 40/69)…");
        KataGoProcess.ActivityFrom("Testing 69 different configs", a, Tr.English).Should().Be(a);
        KataGoProcess.ActivityFrom("Started, ready to begin handling requests", a, Tr.English).Should().BeNull();
    }

    [Fact]
    public void Tuning_started_in_one_language_keeps_progressing_after_a_language_switch()
    {
        string? a = KataGoProcess.ActivityFrom("Performing autotuning", null, Tr.Spanish);
        a = KataGoProcess.ActivityFrom("Tuning 3/69 ...", a, Tr.English);
        a.Should().Be(Tr.T("Engine.Tuning", Tr.English) + " (step 3/69)…");
    }
}
