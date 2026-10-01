using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services.Joseki;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Ogs.Joseki;
using Hoshi.Sgf.Joseki;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>A tiny explorer tree in the top-right corner, like OGS's.</summary>
internal sealed class FakeExplorer : IJosekiExplorerSource
{
    private readonly Dictionary<string, JosekiPosition> _nodes = [];

    public FakeExplorer()
    {
        Add("root", null, true, JosekiCategory.Ideal, null, ("Q16", JosekiCategory.Ideal, "10"), ("R16", JosekiCategory.Ideal, "11"));
        Add("10", "Q16", false, JosekiCategory.Ideal, "The 4-4 point.", ("R17", JosekiCategory.Ideal, "20"), ("R14", JosekiCategory.Good, "21"), ("S18", JosekiCategory.Mistake, "22"));
        Add("20", "R17", false, JosekiCategory.Ideal, "3-3 invasion.", ("Q17", JosekiCategory.Ideal, "30"));
        Add("30", "Q17", false, JosekiCategory.Ideal, null, ("R16", JosekiCategory.Ideal, "40"));
        Add("40", "R16", false, JosekiCategory.Ideal, null, ("R15", JosekiCategory.Ideal, "50"));
        Add("50", "R15", false, JosekiCategory.Ideal, "Settled.", ("pass", JosekiCategory.Ideal, "60"));
        Add("21", "R14", false, JosekiCategory.Good, null);
        Add("22", "S18", false, JosekiCategory.Mistake, null);
        Add("11", "R16", false, JosekiCategory.Ideal, null);
    }

    public int Requests { get; private set; }

    public bool Offline { get; set; }

    public Task<JosekiPosition> GetPositionAsync(string nodeId, CancellationToken cancellationToken)
    {
        Requests++;
        if (Offline)
        {
            throw new HttpRequestException("offline");
        }

        return Task.FromResult(_nodes[nodeId]);
    }

    private void Add(string id, string? at, bool root, JosekiCategory c, string? text, params (string P, JosekiCategory C, string Id)[] next) =>
        _nodes[id] = new JosekiPosition(id, at is null ? null : Point.FromHuman(at, 19), root, c, text,
            [.. next.Select(n => new JosekiNextMove(n.Id, n.P == "pass" ? null : Point.FromHuman(n.P, 19), n.C, null))], []);
}

public sealed class JosekiAssistantTests
{
    private static Point H(string human) => Point.FromHuman(human, 19);

    [Fact]
    public async Task Shows_the_explorers_continuations_in_any_corner()
    {
        var game = new GameViewModel();
        var vm = new JosekiAssistantViewModel(game, explorer: new JosekiExplorer(new FakeExplorer())) { IsOn = true };

        game.PlayCommand.Execute(H("D4")); // the 4-4 point, bottom-left
        await vm.Pending;

        vm.Hints.Should().NotBeNull();
        vm.Hints!.Select(h => h.Point).Should().Contain(H("C3"), "the 3-3 invasion, mirrored into this corner");
        vm.Hints.Should().Contain(h => h.Point == H("C3") && h.Category == JosekiCategory.Ideal);
        vm.Hints.Should().Contain(h => h.Category == JosekiCategory.Mistake && !h.IsRecommended);
        vm.Hints!.Select(h => h.Point).Should().Contain([H("C6"), H("F3")], "the approach from either side");
        vm.Text.Should().Be("Joseki (OGS): ideal · 4 continuaciones");
        vm.Description.Should().Be("The 4-4 point.");
        vm.Note.Should().Be("The 4-4 point.");
        vm.Legend.Should().HaveCount(6);
        vm.Legend.Select(l => l.Label).Should().Equal("Ideal", "Buena", "Truco", "Dudosa", "Error", "Tu biblioteca");
    }

    [Fact]
    public async Task Leaving_the_known_tree_and_going_offline_are_reported()
    {
        var game = new GameViewModel();
        var source = new FakeExplorer();
        var vm = new JosekiAssistantViewModel(game, explorer: new JosekiExplorer(source)) { IsOn = true };

        game.PlayCommand.Execute(H("Q16"));
        game.PlayCommand.Execute(H("O16")); // not in the fake tree
        await vm.Pending;
        vm.Text.Should().Be("Esta esquina ya salió del joseki conocido");

        source.Offline = true;
        game.PlayCommand.Execute(H("D4"));
        await vm.Pending;
        vm.Text.Should().StartWith("Joseki Explorer de OGS no disponible");
    }

    [Fact]
    public async Task A_tenuki_is_followed_like_the_explorer_does()
    {
        var game = new GameViewModel();
        var vm = new JosekiAssistantViewModel(game, explorer: new JosekiExplorer(new FakeExplorer())) { IsOn = true };

        foreach (string m in new[] { "Q16", "R17", "Q17", "R16", "R15" })
        {
            game.PlayCommand.Execute(H(m));
        }

        await vm.Pending;
        vm.Hints.Should().BeEmpty("after R15 the explorer only suggests tenuki");
        vm.Text.Should().Be("Joseki (OGS): ideal · 0 continuaciones");
    }

    [Fact]
    public async Task Local_lines_show_in_violet_and_everything_stops_when_off()
    {
        string dir = Directory.CreateTempSubdirectory("hoshi-hints").FullName;
        try
        {
            var game = new GameViewModel();
            var library = new JosekiLibrary(dataDirectory: dir);
            var vm = new JosekiAssistantViewModel(game, library) { IsOn = true };

            game.PlayCommand.Execute(H("Q16"));
            await vm.Pending;
            vm.Hints.Should().ContainSingle().Which.Should().Be(new BoardJosekiHint(H("R17"), JosekiCategory.Unknown, true));
            vm.Text.Should().Be("Joseki (tu biblioteca) · 1 continuaciones");
            vm.Note.Should().Be("Sigue: Invasión en 3-3 bajo el hoshi");

            foreach (string m in new[] { "R17", "Q17", "R16", "R15", "S15", "S14", "S16" })
            {
                game.PlayCommand.Execute(H(m));
            }

            await vm.Pending;
            vm.Note.Should().StartWith("Completa «Invasión en 3-3 bajo el hoshi»: Blancas viven");

            vm.ToggleCommand.Execute(null);
            await vm.Pending;
            vm.Hints.Should().BeNull();
            vm.Text.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class JosekiTrainerOgsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hoshi-trainer-ogs").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task A_new_line_from_ogs_follows_only_good_moves_and_is_kept()
    {
        var library = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        var vm = new JosekiTrainerViewModel(library, explorer: new JosekiExplorer(new FakeExplorer()), random: new Random(1));
        vm.SelectedFamily = vm.Families.Single(f => f.Family == JosekiFamily.Hoshi);

        await vm.FetchFromOgsCommand.ExecuteAsync(null);

        vm.IsActive.Should().BeTrue();
        vm.StatusText.Should().Be("Línea nueva del Joseki Explorer de OGS. Te toca.");
        JosekiLine line = vm.Drill!.Line;
        line.Moves[0].Point.Should().Be(H("Q16"));
        line.Moves.Select(m => m.Point).Should().NotContain(H("S18"), "mistakes are never part of a line");
        line.Moves.Should().HaveCount(5, "the only long branch ends where the explorer says tenuki");
        line.Comment.Should().Be("Settled.");
        new JosekiLibrary(dataDirectory: _dir, includeStarter: false).Lines.Should().ContainSingle();
    }

    [Fact]
    public void Lines_can_be_picked_from_the_list_and_filtered_by_family()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        library.AddLine([new(Stone.Black, H("R16")), new(Stone.White, H("Q17"))], 19, "Komoku line");
        var vm = new JosekiTrainerViewModel(library, random: new Random(2));
        vm.Open();

        vm.Rows.Should().HaveCount(2);
        vm.SelectedFamily = vm.Families.Single(f => f.Family == JosekiFamily.Komoku);
        vm.Rows.Should().ContainSingle().Which.Name.Should().Be("Komoku line");
        vm.Drill!.Line.Name.Should().Be("Komoku line", "switching family starts a line of that family");

        vm.SelectedFamily = vm.Families[0];
        vm.SelectedLine = vm.Rows.First(r => r.Line.Name.StartsWith("Joseki.Starter", StringComparison.Ordinal));
        vm.LineName.Should().Be("Invasión en 3-3 bajo el hoshi");
        vm.CloseCommand.Execute(null);
        vm.IsActive.Should().BeFalse();
    }

    private static Point H(string human) => Point.FromHuman(human, 19);
}

public sealed class JosekiMainWindowTests
{
    [AvaloniaFact]
    public async Task The_trainer_takes_the_main_board_and_gives_it_back()
    {
        string dir = Directory.CreateTempSubdirectory("hoshi-joseki-ui").FullName;
        try
        {
            var game = new GameViewModel();
            game.PlayCommand.Execute(new Point(9, 9));
            var trainer = new JosekiTrainerViewModel(new JosekiLibrary(dataDirectory: dir), game, random: new Random(3));
            var hints = new JosekiAssistantViewModel(game, explorer: new JosekiExplorer(new FakeExplorer()));
            var vm = new MainWindowViewModel(game, joseki: trainer, josekiHints: hints);
            var window = new MainWindow(vm) { Width = 1300, Height = 820 };
            window.Show();

            vm.OpenJosekiCommand.Execute(null);
            JosekiDrill drill = trainer.Drill!;
            for (int i = 0; i < 4; i++)
            {
                trainer.PlayCommand.Execute(drill.Expected!.Value);
            }

            trainer.PlayCommand.Execute(new Point(9, 3));
            trainer.PlayCommand.Execute(new Point(9, 3));
            await Settle();

            window.FindControl<GoBoardControl>("Board")!.IsVisible.Should().BeFalse();
            window.FindControl<GoBoardControl>("JosekiBoard")!.IsVisible.Should().BeTrue();
            window.FindControl<Border>("JosekiPanel")!.IsVisible.Should().BeTrue();
            window.FindControl<Border>("BottomBar")!.IsVisible.Should().BeFalse();
            window.FindControl<TextBlock>("JosekiLineName")!.Text.Should().Be("Invasión en 3-3 bajo el hoshi");
            Save(window, "joseki-trainer.png");

            trainer.CloseCommand.Execute(null);
            game.Board[new Point(9, 9)].Should().Be(Stone.Black, "the game was left untouched");
            hints.IsOn = true;
            game.PlayCommand.Execute(Point.FromHuman("Q16", 19));
            await hints.Pending;
            await Settle();
            window.FindControl<GoBoardControl>("Board")!.IsVisible.Should().BeTrue();
            window.FindControl<GoBoardControl>("Board")!.JosekiHints.Should().NotBeEmpty();
            window.FindControl<TextBlock>("JosekiText")!.Text.Should().StartWith("Joseki (OGS)");
            window.FindControl<Border>("JosekiInfo")!.IsVisible.Should().BeTrue();
            window.FindControl<TextBlock>("JosekiNote")!.Text.Should().Be("The 4-4 point.");
            Save(window, "joseki-hints.png");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        static async Task Settle()
        {
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(20);
            }
        }

        static void Save(Window window, string name)
        {
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
        }
    }
}
