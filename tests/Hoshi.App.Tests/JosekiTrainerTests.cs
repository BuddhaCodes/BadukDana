using Hoshi.App.Services;
using Hoshi.App.Services.Joseki;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.Tests;

public sealed class JosekiTrainerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-joseki-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void The_starter_library_ships_the_3_3_invasion()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);

        library.Lines.Should().NotBeEmpty();
        library.Lines.Select(l => JosekiTrainerViewModel.Localize(l.Name)).Should().Contain("Invasión en 3-3 bajo el hoshi");
    }

    [Fact]
    public void Imports_an_sgf_collection_and_counts_only_new_lines()
    {
        var library = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        string file = Path.Combine(_dir, "..", Path.GetFileName(_dir) + "-in.sgf");
        File.WriteAllText(file, "(;SZ[19];B[pd](;W[qc];B[pc];W[qd])(;W[nc];B[qf]))");
        try
        {
            library.Import(file).Should().Be(2);
            library.Lines.Should().HaveCount(2);
            library.Import(file).Should().Be(0, "the same lines are known already");
            new JosekiLibrary(dataDirectory: _dir, includeStarter: false).Lines.Should().HaveCount(2, "imports are kept");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Importing_a_file_without_lines_fails()
    {
        var library = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "empty.txt");
        File.WriteAllText(file, "(;SZ[19])");

        FluentActions.Invoking(() => library.Import(file)).Should().Throw<FormatException>();
    }

    [Fact]
    public void Lines_added_from_the_board_are_kept_and_merged_into_one_file()
    {
        var library = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        JosekiMove[] a = [new(Stone.Black, P("pd")), new(Stone.White, P("qc")), new(Stone.Black, P("pc"))];
        JosekiMove[] b = [new(Stone.Black, P("pd")), new(Stone.White, P("nc"))];

        library.AddLine(a, 19, "A").Should().NotBeNull();
        library.AddLine(b, 19, "B").Should().NotBeNull();
        library.AddLine(a, 19, "again").Should().BeNull("it is known");
        library.AddLine(a[..1], 19, "short").Should().BeNull();

        var reopened = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        reopened.Lines.Select(l => l.Name).Should().BeEquivalentTo("A", "B");
        GameTree tree = SgfParser.Parse(File.ReadAllBytes(Path.Combine(reopened.UserDirectory, JosekiLibrary.MyLinesFile)));
        tree.Root.Children.Should().ContainSingle("both lines share their first move");
    }

    [Fact]
    public void Progress_survives_a_restart()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        string id = library.Lines[0].Id;
        library.Record(JosekiCard.New(id).Review(true, _now));

        new JosekiLibrary(dataDirectory: _dir).Cards[id].Box.Should().Be(2);
    }

    [Fact]
    public void A_perfect_run_moves_the_line_up_a_box()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        var sounds = new FakeSoundService();
        JosekiTrainerViewModel vm = Trainer(library, sounds);
        vm.Start();
        JosekiDrill drill = vm.Drill!;

        while (!drill.IsComplete)
        {
            vm.PlayCommand.Execute(drill.Expected!.Value);
        }

        library.Cards[drill.Line.Id].Box.Should().Be(2);
        vm.IsStatusGood.Should().BeTrue();
        vm.StatusText.Should().Be("¡Perfecto! Esta línea volverá más adelante.");
        vm.HasNote.Should().BeTrue();
        sounds.Played.Should().Contain(p => p.Effect == SoundEffect.ImpactSmall);
    }

    [Fact]
    public void A_mistake_sends_the_line_back_to_box_one_and_two_misses_show_the_answer()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        string id = library.Lines[0].Id;
        library.Record(JosekiCard.New(id).Review(true, _now.AddDays(-30)).Review(true, _now.AddDays(-20)));
        JosekiTrainerViewModel vm = Trainer(library);
        vm.Start();
        JosekiDrill drill = vm.Drill!;
        drill.Line.Id.Should().Be(id, "it is the only line and it is due");
        vm.BoxText.Should().Be("Caja 3 de 6");

        Point far = Enumerable.Range(0, 19).Select(x => new Point(x, 9)).First(p => p != drill.Expected);
        vm.PlayCommand.Execute(far);
        vm.IsStatusBad.Should().BeTrue();
        vm.Markers.Should().ContainSingle(m => m.Kind == MarkupKind.Cross);
        vm.PlayCommand.Execute(far);
        vm.Markers.Should().Contain(m => m.Kind == MarkupKind.Circle && m.Point == drill.Expected);

        while (!drill.IsComplete)
        {
            vm.PlayCommand.Execute(drill.Expected!.Value);
        }

        library.Cards[id].Box.Should().Be(1);
        library.Cards[id].Lapses.Should().Be(1);
    }

    [Fact]
    public void Showing_the_answer_spoils_a_perfect_run()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        JosekiTrainerViewModel vm = Trainer(library);
        vm.Start();
        vm.ShowAnswerCommand.Execute(null);
        vm.Markers.Should().ContainSingle(m => m.Kind == MarkupKind.Circle && m.Point == vm.Drill!.Expected);
        JosekiDrill drill = vm.Drill!;

        while (!drill.IsComplete)
        {
            vm.PlayCommand.Execute(drill.Expected!.Value);
        }

        library.Cards[drill.Line.Id].Box.Should().Be(1);
    }

    [Fact]
    public void With_nothing_due_it_offers_extra_practice()
    {
        var library = new JosekiLibrary(dataDirectory: _dir);
        foreach (JosekiLine line in library.Lines)
        {
            library.Record(JosekiCard.New(line.Id).Review(true, _now));
        }

        JosekiTrainerViewModel vm = Trainer(library);
        vm.Start();

        vm.Drill.Should().NotBeNull();
        vm.StatusText.Should().Be("Nada pendiente por ahora: práctica extra.");
        vm.LibraryText.Should().Contain("0 pendientes");
    }

    [Fact]
    public void Adds_the_line_on_the_main_board()
    {
        var library = new JosekiLibrary(dataDirectory: _dir, includeStarter: false);
        var game = new GameViewModel();
        game.PlayCommand.Execute(P("dd"));
        game.PlayCommand.Execute(P("cc"));
        game.PlayCommand.Execute(P("dc"));
        JosekiTrainerViewModel vm = Trainer(library, game: game);

        vm.AddFromBoardCommand.Execute(null);

        library.Lines.Should().ContainSingle().Which.Moves.Should().HaveCount(3);
        vm.StatusText.Should().Be("Línea añadida a tu biblioteca.");
        vm.AddFromBoardCommand.Execute(null);
        vm.StatusText.Should().Be("Esa línea ya está en tu biblioteca.");
    }

    private JosekiTrainerViewModel Trainer(IJosekiLibrary library, FakeSoundService? sounds = null, GameViewModel? game = null) =>
        new(library, game, sounds, new TestSettings(), clock: () => _now, random: new Random(7));

    private static Point P(string sgf) => Point.FromSgf(sgf);
}
