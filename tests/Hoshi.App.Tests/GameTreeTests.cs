using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class GameTreeTests
{
    [Fact]
    public void Main_line_is_column_zero_and_variations_go_to_the_right()
    {
        GameTree tree = SgfParser.Parse("(;SZ[9];B[ee](;W[cc];B[gg])(;W[gc];B[cg])(;W[cg]))");
        GameTreeLayout layout = GameTreeLayout.Compute(tree.Root);

        GameNode b = tree.Root.Children[0];
        layout.Positions[tree.Root].Should().Be((0, 0));
        layout.Positions[b].Should().Be((0, 1));
        layout.Positions[b.Children[0]].Should().Be((0, 2));
        layout.Positions[b.Children[0].Children[0]].Should().Be((0, 3));
        layout.Positions[b.Children[1]].Column.Should().BeGreaterThan(0);
        layout.Positions[b.Children[2]].Column.Should().BeGreaterThan(layout.Positions[b.Children[1]].Column);
        layout.Rows.Should().Be(4);
    }

    [Fact]
    public void No_two_nodes_share_a_cell_in_the_sample_file()
    {
        GameTree tree = SgfParser.Parse(File.ReadAllBytes(GameViewModelPhase3Tests.SamplePath));

        GameTreeLayout layout = GameTreeLayout.Compute(tree.Root);

        layout.Positions.Should().HaveCount(28);
        layout.Positions.Values.Should().OnlyHaveUniqueItems();
        foreach ((GameNode node, (int Column, int Row) pos) in layout.Positions)
        {
            if (node.Parent is { } parent)
            {
                pos.Row.Should().Be(layout.Positions[parent].Row + 1);
                pos.Column.Should().BeGreaterThanOrEqualTo(layout.Positions[parent].Column);
                if (parent.Children[0] == node)
                {
                    pos.Column.Should().Be(layout.Positions[parent].Column, "a line continues straight down");
                }
            }
        }
    }

    [Fact]
    public void Layout_handles_very_long_games()
    {
        var tree = GameTree.Create(19);
        GameNode n = tree.Root;
        for (int i = 0; i < 3000; i++)
        {
            n = n.AddChild();
        }

        GameTreeLayout.Compute(tree.Root).Rows.Should().Be(3001);
    }

    [AvaloniaFact]
    public async Task Clicking_a_node_in_the_tree_panel_navigates()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        await vm.Game.OpenFileAsync(GameViewModelPhase3Tests.SamplePath);
        window.UpdateLayout();

        GameTreeControl tree = window.FindControl<GameTreeControl>("GameTree")!;
        GameNode target = vm.Game.Tree.Root.Children[0].Children[0].Children[0]; // B[pp]
        Avalonia.Point local = GameTreeControl.CenterOf(tree.Layout!.Positions[target]);
        Avalonia.Point at = tree.TranslatePoint(local, window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        vm.Game.CurrentNode.Should().BeSameAs(target);
        vm.Game.MoveNumber.Should().Be(3);
    }

    [AvaloniaFact]
    public void Arrow_keys_navigate_and_p_does_not_pass_while_typing_a_comment()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        vm.Game.PlayCommand.Execute(new Point(3, 3));
        vm.Game.PlayCommand.Execute(new Point(15, 15));

        window.FindControl<GoBoardControl>("Board")!.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        vm.Game.MoveNumber.Should().Be(1);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        vm.Game.MoveNumber.Should().Be(2);

        window.FindControl<Avalonia.Controls.TextBox>("CommentBox")!.Focus();
        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
        window.KeyTextInput("p");

        vm.Game.MoveNumber.Should().Be(2, "P typed in the comment box must not pass");
        vm.Game.Comment.Should().Be("p");
    }

    [AvaloniaFact]
    public async Task Full_window_with_sample_file_is_saved_for_review()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        await vm.Game.OpenFileAsync(GameViewModelPhase3Tests.SamplePath);
        for (int i = 0; i < 7; i++)
        {
            vm.Game.GoForwardCommand.Execute(null); // to B[jp], which carries markup
        }

        WriteableBitmap frame = window.CaptureRenderedFrame()!;
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, "phase3-window.png"));

        vm.Game.Markers.Should().HaveCount(6);
        vm.Game.Board[Point.FromSgf("jp")].Should().Be(Stone.Black);
    }
}
