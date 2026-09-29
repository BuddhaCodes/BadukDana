using Hoshi.Core;

namespace Hoshi.Sgf.Tests;

public sealed class SgfWriterTests
{
    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", "variations.sgf");

    [Fact]
    public void Writes_header_properties_for_a_new_game()
    {
        var tree = GameTree.Create(19);
        tree.Root.AddChild().SetValue("B", "pd");

        string sgf = SgfWriter.Write(tree);

        sgf.Should().StartWith("(;FF[4]GM[1]CA[UTF-8]AP[Hoshi:");
        sgf.Should().Contain("SZ[19]");
        sgf.Should().Contain(";B[pd]");
        sgf.TrimEnd().Should().EndWith(")");
    }

    [Fact]
    public void Escapes_closing_brackets_and_backslashes()
    {
        var tree = GameTree.Create(9);
        tree.Root.SetValue("C", @"a ] b \ c");

        SgfWriter.Write(tree).Should().Contain(@"C[a \] b \\ c]");
    }

    [Fact]
    public void Writes_variations_in_parentheses()
    {
        GameTree tree = SgfParser.Parse("(;SZ[9];B[ee](;W[cc])(;W[gg]))");

        string sgf = SgfWriter.Write(tree);

        sgf.Should().MatchRegex(@";B\[ee\]\s*\(;W\[cc\]\)\s*\(;W\[gg\]\)\)");
    }

    [Fact]
    public void Property_order_is_deterministic()
    {
        GameTree a = SgfParser.Parse("(;C[x]SZ[9]PB[b]AB[aa]KM[6.5])");
        GameTree b = SgfParser.Parse("(;KM[6.5]AB[aa]PB[b]SZ[9]C[x])");

        string sgf = SgfWriter.Write(a);

        sgf.Should().Be(SgfWriter.Write(b));
        sgf.IndexOf("SZ[", StringComparison.Ordinal).Should().BeLessThan(sgf.IndexOf("KM[", StringComparison.Ordinal));
        sgf.IndexOf("AB[", StringComparison.Ordinal).Should().BeLessThan(sgf.IndexOf("C[x]", StringComparison.Ordinal));
    }

    [Fact]
    public void Existing_application_name_is_replaced_by_hoshi()
    {
        GameTree tree = SgfParser.Parse("(;AP[OtherApp:2]SZ[9])");

        SgfWriter.Write(tree).Should().Contain("AP[Hoshi:").And.NotContain("OtherApp");
    }

    [Fact]
    public void Sample_file_round_trips_without_loss()
    {
        GameTree original = SgfParser.Parse(File.ReadAllBytes(SamplePath));

        GameTree reparsed = SgfParser.Parse(SgfWriter.Write(original));

        TreeShouldMatch(original.Root, reparsed.Root, ignore: ["AP"]);
    }

    [Fact]
    public void Writing_is_idempotent()
    {
        string once = SgfWriter.Write(SgfParser.Parse(File.ReadAllBytes(SamplePath)));
        string twice = SgfWriter.Write(SgfParser.Parse(once));

        twice.Should().Be(once);
    }

    [Fact]
    public void A_long_random_game_round_trips()
    {
        var random = new Random(1234);
        var tree = GameTree.Create(19);
        var cursor = new GameCursor(tree);
        int played = 0;
        while (played < 250)
        {
            var p = new Point(random.Next(19), random.Next(19));
            if (cursor.Play(p).IsLegal)
            {
                played++;
            }
        }

        GameTree reparsed = SgfParser.Parse(SgfWriter.Write(tree));
        var reparsedCursor = new GameCursor(reparsed);
        reparsedCursor.Last();

        reparsedCursor.MoveNumber.Should().Be(250);
        reparsedCursor.Board.Hash.Should().Be(cursor.Board.Hash);
    }

    private static void TreeShouldMatch(GameNode expected, GameNode actual, string[] ignore)
    {
        actual.PropertyIds.Except(ignore).Should().BeEquivalentTo(expected.PropertyIds.Except(ignore));
        foreach (string id in expected.PropertyIds.Except(ignore))
        {
            actual.GetValues(id).Should().Equal(expected.GetValues(id), $"property {id} must survive");
        }

        actual.Children.Should().HaveCount(expected.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            TreeShouldMatch(expected.Children[i], actual.Children[i], []);
        }
    }
}
