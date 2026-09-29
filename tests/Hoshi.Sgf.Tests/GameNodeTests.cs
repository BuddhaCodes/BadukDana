using Hoshi.Core;

namespace Hoshi.Sgf.Tests;

public sealed class GameNodeTests
{
    private static GameNode Node(string props) => SgfParser.Parse($"(;{props})").Root;

    [Theory]
    [InlineData("B[pd]", Stone.Black, 15, 3)]
    [InlineData("W[dp]", Stone.White, 3, 15)]
    public void Reads_moves(string props, Stone color, int x, int y)
    {
        SgfMove? move = Node(props).GetMove(19);

        move.Should().Be(new SgfMove(color, new Point(x, y)));
        move!.Value.IsPass.Should().BeFalse();
    }

    [Theory]
    [InlineData("B[]", 19)]
    [InlineData("W[tt]", 19)]
    [InlineData("B[tt]", 9)]
    public void Reads_passes(string props, int size)
    {
        Node(props).GetMove(size)!.Value.IsPass.Should().BeTrue();
    }

    [Fact]
    public void Nodes_without_moves_return_null()
    {
        Node("C[setup only]AB[aa]").GetMove(19).Should().BeNull();
    }

    [Fact]
    public void Compressed_point_lists_are_expanded()
    {
        Node("AB[aa:bc][dd]").GetPoints("AB").Should().BeEquivalentTo(
            [new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1), new Point(0, 2), new Point(1, 2), new Point(3, 3)]);
    }

    [Fact]
    public void Markup_is_read_from_all_shape_properties()
    {
        GameNode node = Node("TR[aa]SQ[bb]CR[cc]MA[dd]LB[ee:A][ff:12]");

        node.GetMarkup().Should().BeEquivalentTo(new Markup[]
        {
            new(new Point(0, 0), MarkupKind.Triangle),
            new(new Point(1, 1), MarkupKind.Square),
            new(new Point(2, 2), MarkupKind.Circle),
            new(new Point(3, 3), MarkupKind.Cross),
            new(new Point(4, 4), MarkupKind.Label, "A"),
            new(new Point(5, 5), MarkupKind.Label, "12"),
        });
    }

    [Fact]
    public void Setting_and_removing_properties()
    {
        var node = new GameNode();

        node.SetValue("C", "hello");
        node.AddValue("AB", "aa");
        node.AddValue("AB", "bb");
        node.GetValues("AB").Should().Equal("aa", "bb");

        node.RemoveValue("AB", "aa");
        node.GetValues("AB").Should().Equal("bb");

        node.RemoveProperty("C");
        node.HasProperty("C").Should().BeFalse();

        node.RemoveValue("AB", "bb");
        node.HasProperty("AB").Should().BeFalse("empty properties are removed");
    }

    [Fact]
    public void Property_identifiers_must_be_upper_case_letters()
    {
        FluentActions.Invoking(() => new GameNode().SetValue("b", "aa")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Children_can_be_added_inserted_and_detached()
    {
        var root = new GameNode();
        GameNode a = root.AddChild();
        GameNode b = root.AddChild();
        GameNode c = root.InsertChild(0, new GameNode());

        root.Children.Should().Equal(c, a, b);
        b.Detach();
        root.Children.Should().Equal(c, a);
        b.Parent.Should().BeNull();
        root.MoveChild(a, 0);
        root.Children.Should().Equal(a, c);
    }

    [Fact]
    public void Game_info_reads_the_root()
    {
        GameTree tree = SgfParser.Parse(
            "(;SZ[19:13]KM[7.5]HA[2]RU[Chinese]PB[Kuro]BR[3k]PW[Shiro]WR[2d]DT[2026-09-29]EV[Cup]RE[W+R]GN[Final]PC[Havana])");
        GameInfo info = tree.Info;

        info.Width.Should().Be(19);
        info.Height.Should().Be(13);
        info.Komi.Should().Be(7.5);
        info.Handicap.Should().Be(2);
        info.Rules.Should().BeSameAs(RuleSet.Chinese);
        info.BlackPlayer.Should().Be("Kuro");
        info.BlackRank.Should().Be("3k");
        info.WhitePlayer.Should().Be("Shiro");
        info.WhiteRank.Should().Be("2d");
        info.Date.Should().Be("2026-09-29");
        info.Event.Should().Be("Cup");
        info.Result.Should().Be("W+R");
        info.GameName.Should().Be("Final");
        info.Place.Should().Be("Havana");
    }

    [Theory]
    [InlineData("Japanese", "japanese")]
    [InlineData("AGA", "aga")]
    [InlineData("NZ", "nz")]
    [InlineData("New Zealand", "nz")]
    [InlineData("GOE", "ing")]
    [InlineData("korean", "korean")]
    public void Rule_names_from_other_programs_are_recognised(string ru, string expected)
    {
        SgfParser.Parse($"(;RU[{ru}])").Info.Rules!.Name.Should().Be(expected);
    }

    [Fact]
    public void Game_info_defaults_and_setters()
    {
        var tree = GameTree.Create(13);

        tree.Info.Width.Should().Be(13);
        tree.Info.Komi.Should().BeNull();
        tree.Info.Rules.Should().BeNull();

        tree.Info.BlackPlayer = "Kuro";
        tree.Info.Komi = 6.5;
        tree.Info.WhitePlayer = "";

        tree.Root.GetValue("PB").Should().Be("Kuro");
        tree.Root.GetValue("KM").Should().Be("6.5");
        tree.Root.HasProperty("PW").Should().BeFalse("empty strings clear the property");
    }
}
