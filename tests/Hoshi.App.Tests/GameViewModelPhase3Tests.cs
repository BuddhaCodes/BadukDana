using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class GameViewModelPhase3Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hoshi-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", "variations.sgf");

    private static Point P(string sgf) => Point.FromSgf(sgf);

    [Fact]
    public void Going_back_and_playing_elsewhere_creates_a_variation()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P("ee"));
        game.PlayCommand.Execute(P("cc"));

        game.GoBackCommand.Execute(null);
        game.PlayCommand.Execute(P("gg"));

        GameNode afterBlack = game.Tree.Root.Children.Single();
        afterBlack.Children.Select(c => c.GetValue("W")).Should().Equal("cc", "gg");
        game.GoBackCommand.Execute(null);
        game.NextVariationCommand.Execute(null);
        game.CurrentNode.GetValue("W").Should().BeNull("gg is already the last variation");
    }

    [Fact]
    public void Navigation_commands_move_along_the_tree()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        foreach (string m in new[] { "ee", "cc", "gg", "cg" })
        {
            game.PlayCommand.Execute(P(m));
        }

        game.GoFirstCommand.Execute(null);
        game.MoveNumber.Should().Be(0);
        game.GoBackCommand.CanExecute(null).Should().BeFalse();

        game.GoLastCommand.Execute(null);
        game.MoveNumber.Should().Be(4);

        game.ScrollCommand.Execute(-1);
        game.MoveNumber.Should().Be(3);
        game.ScrollCommand.Execute(1);
        game.MoveNumber.Should().Be(4);
    }

    [Fact]
    public void Clicking_a_tree_node_navigates_to_it()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P("ee"));
        GameNode first = game.CurrentNode;
        game.PlayCommand.Execute(P("cc"));

        game.GoToNodeCommand.Execute(first);

        game.CurrentNode.Should().BeSameAs(first);
        game.Board[P("cc")].Should().Be(Stone.Empty);
    }

    [Fact]
    public void Edit_mode_places_setup_stones_and_markup_instead_of_moves()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.ToggleEditModeCommand.Execute(null);

        game.PlayCommand.Execute(P("cc"));
        game.SelectToolCommand.Execute(EditTool.WhiteStone);
        game.PlayCommand.Execute(P("dd"));
        game.SelectToolCommand.Execute(EditTool.Triangle);
        game.PlayCommand.Execute(P("cc"));
        game.SelectToolCommand.Execute(EditTool.Label);
        game.PlayCommand.Execute(P("ee"));
        game.PlayCommand.Execute(P("ff"));

        game.CurrentNode.Should().BeSameAs(game.Tree.Root, "setup on a node without a move edits that node");
        game.Board[P("cc")].Should().Be(Stone.Black);
        game.Board[P("dd")].Should().Be(Stone.White);
        game.Markers.Should().Contain(new Markup(P("cc"), MarkupKind.Triangle));
        game.Markers.Where(m => m.Kind == MarkupKind.Label).Select(m => m.Text).Should().Equal("A", "B");
        game.StatusText.Should().Be("Modo edición · etiqueta");
        game.IsDirty.Should().BeTrue();
        game.PassCommand.CanExecute(null).Should().BeFalse("passing is a play-mode action");
    }

    [Fact]
    public void Edit_mode_ghost_shows_the_tool_colour()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.SelectToolCommand.Execute(EditTool.WhiteStone);

        game.HoverPoint = P("cc");

        game.GhostStone.Should().Be(Stone.White);
        game.SelectToolCommand.Execute(EditTool.Circle);
        game.GhostStone.Should().Be(Stone.Empty);
    }

    [Fact]
    public void Comments_are_edited_on_the_current_node()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P("ee"));

        game.Comment = "Tengen";

        game.CurrentNode.Comment.Should().Be("Tengen");
        game.IsDirty.Should().BeTrue();
        game.GoBackCommand.Execute(null);
        game.Comment.Should().BeNull();
    }

    [Fact]
    public async Task Opening_the_sample_file_shows_its_first_position_and_players()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);

        (await game.OpenFileAsync(SamplePath)).Should().BeTrue();

        game.Board.Width.Should().Be(19);
        game.BlackName.Should().Be("Kuro 3k");
        game.WhiteName.Should().Be("Shiro 2k");
        game.Title.Should().Be("variations.sgf — Hoshi");
        game.Comment.Should().StartWith("Sample written for the Hoshi test suite");
        game.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Open_edit_save_and_reopen_keeps_everything()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        await game.OpenFileAsync(SamplePath);

        // Edit: follow the attach variation, add a comment and a new variation with markup.
        game.GoForwardCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        game.NextVariationCommand.Execute(null);
        game.NextVariationCommand.Execute(null);
        game.CurrentNode.GetValue("B").Should().Be("dq");
        game.Comment = "Edited in Hoshi: añadido un comentario.";
        game.PlayCommand.Execute(P("dr"));
        game.SelectToolCommand.Execute(EditTool.Square);
        game.PlayCommand.Execute(P("dq"));
        game.ToggleEditModeCommand.Execute(null);

        string path = Path.Combine(_dir, "edited.sgf");
        await game.SaveToAsync(path);
        game.IsDirty.Should().BeFalse();

        var reopened = new GameViewModel(9, RuleSet.Japanese);
        await reopened.OpenFileAsync(path);

        string original = SgfWriter.Write(game.Tree);
        string roundTripped = SgfWriter.Write(reopened.Tree);
        roundTripped.Should().Be(original);
        reopened.Tree.AllNodes().Count().Should().Be(29);
        reopened.Tree.AllNodes().Should().Contain(n => n.Comment == "Edited in Hoshi: añadido un comentario.");
    }

    [Fact]
    public async Task Unreadable_files_report_an_error_and_keep_the_current_game()
    {
        var dialogs = new FakeDialogs();
        var game = new GameViewModel(null, dialogs, null);
        game.PlayCommand.Execute(P("dd"));
        string bad = Path.Combine(_dir, "bad.sgf");
        await File.WriteAllTextAsync(bad, "this is not sgf");

        (await game.OpenFileAsync(bad, confirm: false)).Should().BeFalse();

        dialogs.Errors.Should().ContainSingle();
        game.Board[P("dd")].Should().Be(Stone.Black);
    }

    [Fact]
    public async Task Unsaved_changes_are_confirmed_before_being_discarded()
    {
        var dialogs = new FakeDialogs { ConfirmAnswer = false };
        var game = new GameViewModel(null, dialogs, null);
        game.PlayCommand.Execute(P("dd"));

        await game.NewGameCommand.ExecuteAsync(9);

        dialogs.Confirmations.Should().Be(1);
        game.Board[P("dd")].Should().Be(Stone.Black, "the user chose not to discard");

        dialogs.ConfirmAnswer = true;
        await game.NewGameCommand.ExecuteAsync(9);
        game.Board.Width.Should().Be(9);
        game.MoveNumber.Should().Be(0);
    }

    [Fact]
    public async Task Save_without_a_path_asks_for_one()
    {
        var files = new FakeFiles { SavePath = Path.Combine(_dir, "picked.sgf") };
        var game = new GameViewModel(files, null, null);
        game.PlayCommand.Execute(P("pd"));

        await game.SaveCommand.ExecuteAsync(null);

        files.SuggestedNames.Should().Equal("Negras vs Blancas.sgf");
        File.Exists(files.SavePath).Should().BeTrue();
        game.FilePath.Should().Be(files.SavePath);
    }

    [Fact]
    public void Game_info_edits_are_applied_to_the_root()
    {
        var game = new GameViewModel(19, RuleSet.Japanese);
        var info = new GameInfoViewModel(game.Tree.Info) { BlackPlayer = "Kuro", BlackRank = "1d", Komi = "6,5", Rules = RuleSet.Chinese };

        game.ApplyGameInfo(info);

        game.Tree.Root.GetValue("PB").Should().Be("Kuro");
        game.Tree.Root.GetValue("KM").Should().Be("6.5");
        game.Tree.Root.GetValue("RU").Should().Be("Chinese");
        game.BlackName.Should().Be("Kuro 1d");
        game.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Invalid_komi_disables_ok()
    {
        var info = new GameInfoViewModel { Komi = "abc" };

        info.KomiIsValid.Should().BeFalse();
        info.Komi = "0.5";
        info.KomiIsValid.Should().BeTrue();
    }

    [Fact]
    public void Deleting_and_promoting_variations()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        game.PlayCommand.Execute(P("ee"));
        game.PlayCommand.Execute(P("cc"));
        game.GoBackCommand.Execute(null);
        game.PlayCommand.Execute(P("gg"));

        game.PromoteVariationCommand.Execute(null);
        game.Tree.Root.Children[0].Children[0].GetValue("W").Should().Be("gg");

        game.DeleteNodeCommand.Execute(null);
        game.Tree.Root.Children[0].Children.Select(c => c.GetValue("W")).Should().Equal("cc");
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmAnswer { get; set; } = true;

        public int Confirmations { get; private set; }

        public List<string> Errors { get; } = [];

        public Task<bool> EditGameInfoAsync(GameInfoViewModel info) => Task.FromResult(false);

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Confirmations++;
            return Task.FromResult(ConfirmAnswer);
        }

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFiles : IFileDialogService
    {
        public string? SavePath { get; init; }

        public List<string> SuggestedNames { get; } = [];

        public Task<string?> PickSgfToOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickSgfToSaveAsync(string suggestedName)
        {
            SuggestedNames.Add(suggestedName);
            return Task.FromResult(SavePath);
        }
    }
}
