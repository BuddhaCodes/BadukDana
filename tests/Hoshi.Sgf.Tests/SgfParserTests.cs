using System.Text;

namespace Hoshi.Sgf.Tests;

public sealed class SgfParserTests
{
    [Fact]
    public void Parses_root_properties_and_a_main_line()
    {
        GameTree tree = SgfParser.Parse("(;FF[4]GM[1]SZ[19];B[pd];W[dp])");

        tree.Root.GetValue("SZ").Should().Be("19");
        tree.Root.Children.Should().ContainSingle();
        GameNode b = tree.Root.Children[0];
        b.GetValue("B").Should().Be("pd");
        b.Children.Single().GetValue("W").Should().Be("dp");
        b.Children.Single().Parent.Should().BeSameAs(b);
    }

    [Fact]
    public void Parses_variations_as_children_in_order()
    {
        GameTree tree = SgfParser.Parse("(;SZ[9];B[ee](;W[cc];B[gg])(;W[gc])(;W[cg]))");

        GameNode b = tree.Root.Children.Single();
        b.Children.Select(c => c.GetValue("W")).Should().Equal("cc", "gc", "cg");
        b.Children[0].Children.Single().GetValue("B").Should().Be("gg");
    }

    [Fact]
    public void Handles_whitespace_and_newlines_everywhere()
    {
        GameTree tree = SgfParser.Parse("  (\n ; FF [4]\r\n SZ\t[9]\n ;\n B [ee] \n ) \n");

        tree.Root.GetValue("FF").Should().Be("4");
        tree.Root.Children.Single().GetValue("B").Should().Be("ee");
    }

    [Fact]
    public void Unescapes_brackets_backslashes_and_soft_line_breaks()
    {
        GameTree tree = SgfParser.Parse("(;C[a \\] b \\\\ c \\\nd])");

        tree.Root.GetValue("C").Should().Be("a ] b \\ c d");
    }

    [Fact]
    public void Keeps_hard_line_breaks_in_text()
    {
        GameTree tree = SgfParser.Parse("(;C[line one\nline two])");

        tree.Root.GetValue("C").Should().Be("line one\nline two");
    }

    [Fact]
    public void Reads_multiple_values()
    {
        GameTree tree = SgfParser.Parse("(;AB[aa][bb][cc]AW[dd])");

        tree.Root.GetValues("AB").Should().Equal("aa", "bb", "cc");
        tree.Root.GetValues("AW").Should().Equal("dd");
        tree.Root.GetValues("XX").Should().BeEmpty();
    }

    [Fact]
    public void Old_style_long_identifiers_are_reduced_to_their_capitals()
    {
        GameTree tree = SgfParser.Parse("(;AddBlack[aa]Comment[hi])");

        tree.Root.GetValues("AB").Should().Equal("aa");
        tree.Root.GetValue("C").Should().Be("hi");
    }

    [Fact]
    public void Reads_a_collection_of_several_games()
    {
        SgfParseResult result = SgfParser.ParseCollection("(;GN[one])(;GN[two])\n(;GN[three])");

        result.Games.Select(g => g.Root.GetValue("GN")).Should().Equal("one", "two", "three");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Tolerates_garbage_before_the_game_and_a_missing_final_parenthesis()
    {
        SgfParseResult result = SgfParser.ParseCollection("Downloaded from somewhere\n(;SZ[9];B[ee];W[cc]");

        result.Games.Should().ContainSingle();
        result.Games[0].Root.Children.Single().Children.Single().GetValue("W").Should().Be("cc");
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Tolerates_an_unterminated_value()
    {
        SgfParseResult result = SgfParser.ParseCollection("(;SZ[9];C[never closed");

        result.Games.Single().Root.Children.Single().GetValue("C").Should().Be("never closed");
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Tolerates_a_property_without_values()
    {
        SgfParseResult result = SgfParser.ParseCollection("(;SZ[9]XX;B[ee])");

        result.Games.Single().Root.HasProperty("XX").Should().BeFalse();
        result.Games.Single().Root.Children.Single().GetValue("B").Should().Be("ee");
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Empty_input_has_no_games_and_parse_throws()
    {
        SgfParser.ParseCollection("   ").Games.Should().BeEmpty();
        FluentActions.Invoking(() => SgfParser.Parse("no sgf here")).Should().Throw<FormatException>();
    }

    [Fact]
    public void Bytes_default_to_utf8()
    {
        byte[] data = Encoding.UTF8.GetBytes("(;PB[Cho Hun-hyeon 조훈현]PW[李昌镐])");

        GameTree tree = SgfParser.Parse(data);

        tree.Root.GetValue("PB").Should().Be("Cho Hun-hyeon 조훈현");
        tree.Root.GetValue("PW").Should().Be("李昌镐");
    }

    [Fact]
    public void Bytes_honour_the_CA_property()
    {
        byte[] latin1 = Encoding.Latin1.GetBytes("(;CA[ISO-8859-1]PB[José Núñez])");

        SgfParser.Parse(latin1).Root.GetValue("PB").Should().Be("José Núñez");
    }

    [Fact]
    public void Bytes_support_legacy_asian_code_pages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] gb = Encoding.GetEncoding("GB2312").GetBytes("(;CA[GB2312]PB[古力])");

        SgfParser.Parse(gb).Root.GetValue("PB").Should().Be("古力");
    }

    [Fact]
    public void Invalid_utf8_without_CA_falls_back_to_latin1()
    {
        byte[] data = [.. "(;PB["u8.ToArray(), 0xE9, .. "])"u8.ToArray()]; // lone 0xE9 = "é" in Latin-1

        SgfParser.Parse(data).Root.GetValue("PB").Should().Be("é");
    }

    [Fact]
    public void Parses_the_sample_file_with_all_its_variations()
    {
        GameTree tree = SgfParser.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "variations.sgf")));

        tree.Root.GetValue("C").Should().Contain("escaped ] brackets and a back\\slash");
        tree.AllNodes().Count().Should().Be(28);
        GameNode fork = tree.Root.Children[0].Children[0].Children[0].Children[0];
        fork.GetValue("W").Should().Be("dd");
        fork.Children.Should().HaveCount(3);
    }
}
