using Hoshi.Core;

namespace Hoshi.Sgf.Tests;

public sealed class GameCursorTests
{
    private static Point P(string sgf) => Point.FromSgf(sgf);

    private static GameCursor Open(string sgf) => new(SgfParser.Parse(sgf));

    [Fact]
    public void Starts_at_the_root_with_an_empty_board()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[cc])");

        cursor.Current.Should().BeSameAs(cursor.Tree.Root);
        cursor.Board.Width.Should().Be(9);
        cursor.MoveNumber.Should().Be(0);
        cursor.CanGoBack.Should().BeFalse();
        cursor.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void Navigates_forward_back_first_and_last()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[cc];B[gg])");

        cursor.Next().Should().BeTrue();
        cursor.Board[P("ee")].Should().Be(Stone.Black);
        cursor.LastMove.Should().Be(P("ee"));

        cursor.Last();
        cursor.MoveNumber.Should().Be(3);
        cursor.Next().Should().BeFalse();

        cursor.Previous().Should().BeTrue();
        cursor.MoveNumber.Should().Be(2);

        cursor.First();
        cursor.Current.Should().BeSameAs(cursor.Tree.Root);
        cursor.Previous().Should().BeFalse();
    }

    [Fact]
    public void Boards_apply_captures_along_the_path()
    {
        GameCursor cursor = Open("(;SZ[5];B[ba];W[aa];B[ab])");

        cursor.Last();

        cursor.Board[P("aa")].Should().Be(Stone.Empty);
        cursor.Board.BlackCaptures.Should().Be(1);
    }

    [Fact]
    public void Setup_properties_are_applied_and_player_to_move_honours_PL()
    {
        GameCursor cursor = Open("(;SZ[9]AB[aa][bb]AW[cc]PL[W];AE[bb])");

        cursor.Board[P("aa")].Should().Be(Stone.Black);
        cursor.Board[P("cc")].Should().Be(Stone.White);
        cursor.Board.ToMove.Should().Be(Stone.White);

        cursor.Next();
        cursor.Board[P("bb")].Should().Be(Stone.Empty);
    }

    [Fact]
    public void Handicap_root_gives_white_the_first_move()
    {
        Open("(;SZ[19]HA[2]AB[pd][dp])").Board.ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public void Rules_and_rectangular_size_come_from_the_root()
    {
        GameCursor cursor = Open("(;SZ[13:9]RU[Chinese])");

        cursor.Board.Width.Should().Be(13);
        cursor.Board.Height.Should().Be(9);
        cursor.Board.Rules.Should().BeSameAs(RuleSet.Chinese);
    }

    [Fact]
    public void Passes_are_supported()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[])");

        cursor.Last();

        cursor.LastMove.Should().BeNull();
        cursor.Board.ToMove.Should().Be(Stone.Black);
    }

    [Fact]
    public void Illegal_moves_in_a_file_are_forced_and_reported()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[ee])");

        cursor.Last();

        cursor.Board[P("ee")].Should().Be(Stone.White);
        cursor.Warnings.Should().ContainSingle().Which.Should().Contain("ee");
    }

    [Fact]
    public void Switches_between_sibling_variations()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee](;W[cc])(;W[gc])(;W[cg]))");
        cursor.Next();
        cursor.Next();

        cursor.NextVariation().Should().BeTrue();
        cursor.Current.GetValue("W").Should().Be("gc");
        cursor.NextVariation().Should().BeTrue();
        cursor.NextVariation().Should().BeFalse("there is no fourth variation");
        cursor.PreviousVariation().Should().BeTrue();
        cursor.Current.GetValue("W").Should().Be("gc");
    }

    [Fact]
    public void Going_forward_remembers_the_last_visited_variation()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee](;W[cc])(;W[gc]))");
        cursor.Next();
        cursor.Next();
        cursor.NextVariation();

        cursor.Previous();
        cursor.Next();

        cursor.Current.GetValue("W").Should().Be("gc");
    }

    [Fact]
    public void Playing_creates_a_child_node_for_the_player_to_move()
    {
        GameCursor cursor = Open("(;SZ[9])");

        MoveResult result = cursor.Play(P("ee"));

        result.IsLegal.Should().BeTrue();
        cursor.Current.GetValue("B").Should().Be("ee");
        cursor.Tree.Root.Children.Should().ContainSingle();
        cursor.Board.ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public void Playing_an_existing_move_reuses_the_node()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[cc])");
        cursor.Next();

        cursor.Play(P("cc"));

        cursor.Current.Should().BeSameAs(cursor.Tree.Root.Children[0].Children[0]);
        cursor.Tree.Root.Children[0].Children.Should().ContainSingle();
    }

    [Fact]
    public void Playing_a_different_move_adds_a_variation()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[cc])");
        cursor.Next();

        cursor.Play(P("gg"));

        cursor.Tree.Root.Children[0].Children.Select(c => c.GetValue("W")).Should().Equal("cc", "gg");
        cursor.Current.GetValue("W").Should().Be("gg");
    }

    [Fact]
    public void Illegal_play_creates_nothing()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee])");
        cursor.Next();

        MoveResult result = cursor.Play(P("ee"));

        result.Reason.Should().Be(IllegalMoveReason.Occupied);
        cursor.Current.Children.Should().BeEmpty();
    }

    [Fact]
    public void Pass_creates_an_empty_move()
    {
        GameCursor cursor = Open("(;SZ[9])");

        cursor.Play(null);

        cursor.Current.GetValues("B").Should().Equal(string.Empty);
    }

    [Fact]
    public void Setup_stones_toggle_on_a_node_without_a_move()
    {
        GameCursor cursor = Open("(;SZ[9])");

        cursor.ToggleSetupStone(P("cc"), Stone.Black);
        cursor.Board[P("cc")].Should().Be(Stone.Black);
        cursor.Current.GetValues("AB").Should().Equal("cc");

        cursor.ToggleSetupStone(P("cc"), Stone.Black);
        cursor.Board[P("cc")].Should().Be(Stone.Empty);
        cursor.Current.HasProperty("AB").Should().BeFalse();

        cursor.ToggleSetupStone(P("cc"), Stone.White);
        cursor.Board[P("cc")].Should().Be(Stone.White);
    }

    [Fact]
    public void Setup_on_a_move_node_creates_a_child_and_removing_an_earlier_stone_uses_AE()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee])");
        cursor.Next();

        cursor.ToggleSetupStone(P("ee"), Stone.Black);

        cursor.Current.Parent!.GetValue("B").Should().Be("ee");
        cursor.Current.GetValues("AE").Should().Equal("ee");
        cursor.Board[P("ee")].Should().Be(Stone.Empty);
    }

    [Fact]
    public void Editing_an_ancestor_updates_descendant_boards()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee];W[cc])");
        GameNode leaf = cursor.Tree.Root.Children[0].Children[0];
        cursor.GetBoard(leaf)[P("aa")].Should().Be(Stone.Empty);

        cursor.ToggleSetupStone(P("aa"), Stone.White);

        cursor.GetBoard(leaf)[P("aa")].Should().Be(Stone.White);
    }

    [Fact]
    public void Markup_toggles_and_replaces_other_shapes()
    {
        GameCursor cursor = Open("(;SZ[9])");

        cursor.ToggleMarkup(P("cc"), MarkupKind.Triangle);
        cursor.Current.GetMarkup().Should().Equal(new Markup(P("cc"), MarkupKind.Triangle));

        cursor.ToggleMarkup(P("cc"), MarkupKind.Square);
        cursor.Current.GetMarkup().Should().Equal(new Markup(P("cc"), MarkupKind.Square));

        cursor.ToggleMarkup(P("cc"), MarkupKind.Square);
        cursor.Current.GetMarkup().Should().BeEmpty();

        cursor.ToggleMarkup(P("dd"), MarkupKind.Label, "A");
        cursor.Current.GetValues("LB").Should().Equal("dd:A");
    }

    [Fact]
    public void Next_free_label_skips_letters_in_use()
    {
        GameCursor cursor = Open("(;SZ[9]LB[aa:A][bb:C])");

        cursor.NextFreeLabel().Should().Be("B");
    }

    [Fact]
    public void Comments_are_set_and_cleared()
    {
        GameCursor cursor = Open("(;SZ[9])");

        cursor.Comment = "Nice move";
        cursor.Current.GetValue("C").Should().Be("Nice move");

        cursor.Comment = "  ";
        cursor.Current.HasProperty("C").Should().BeFalse();
    }

    [Fact]
    public void Deleting_the_current_node_moves_to_its_parent()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee](;W[cc])(;W[gg]))");
        cursor.Next();
        cursor.Next();

        cursor.DeleteCurrent();

        cursor.Current.GetValue("B").Should().Be("ee");
        cursor.Current.Children.Select(c => c.GetValue("W")).Should().Equal("gg");
    }

    [Fact]
    public void Promoting_makes_the_current_line_the_main_line()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee](;W[cc])(;W[gg];B[cg]))");
        cursor.Next();
        cursor.Next();
        cursor.NextVariation();
        cursor.Next();

        cursor.PromoteToMainLine();

        cursor.Tree.Root.Children[0].Children[0].GetValue("W").Should().Be("gg");
        cursor.IsOnMainLine.Should().BeTrue();
    }

    [Fact]
    public void Changed_is_raised_on_navigation_and_edits()
    {
        GameCursor cursor = Open("(;SZ[9];B[ee])");
        int changes = 0;
        cursor.Changed += (_, _) => changes++;

        cursor.Next();
        cursor.Comment = "x";
        cursor.Next();

        changes.Should().Be(2, "a failed navigation does not raise Changed");
    }

    [Fact]
    public void Very_long_games_do_not_overflow_the_stack()
    {
        var tree = GameTree.Create(19);
        GameNode node = tree.Root;
        for (int i = 0; i < 5000; i++)
        {
            node = node.AddChild();
            node.SetValue(i % 2 == 0 ? "B" : "W", string.Empty);
        }

        var cursor = new GameCursor(tree);
        cursor.Last();

        cursor.MoveNumber.Should().Be(5000);
    }

    [Fact]
    public void Board_sizes_above_25_are_rejected()
    {
        FluentActions.Invoking(() => Open("(;SZ[39])")).Should().Throw<NotSupportedException>();
    }
}
