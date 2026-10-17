using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hoshi.App.Controls;
using Hoshi.App.Services.Tsumego;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf.Problems;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

[Collection("Language")]
public sealed class TsumegoTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hoshi-tsumego").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Plays the answer (the hint) until the problem is solved; returns the final board.</summary>
    private static BoardState Solve(TsumegoProblem p)
    {
        var attempt = new TsumegoAttempt(p);
        for (int i = 0; i < 20 && !attempt.IsFinished; i++)
        {
            attempt.Hint.Should().NotBeNull($"{p.Name} has an answer at every step");
            attempt.Play(attempt.Hint!.Value).Should().NotBe(TsumegoOutcome.Wrong, p.Name);
        }

        attempt.Result.Should().Be(TsumegoOutcome.Solved, p.Name);
        return attempt.Board;
    }

    [AvaloniaFact]
    public void Hoshi_problems_are_named_and_their_answers_do_what_they_say()
    {
        TsumegoSource builtIn = TsumegoLibrary.BuiltIn();
        builtIn.Problems.Should().HaveCount(8);
        builtIn.Problems.Should().OnlyContain(p => Tr.Has(p.Name) && Tr.Has(p.Instructions!));

        TsumegoProblem P(int n) => builtIn.Problems[n - 1];

        // Capturing problems: the white stones are gone at the end of the answer.
        foreach (int n in new[] { 1, 2, 3, 4 })
        {
            BoardState start = P(n).Start;
            BoardState end = Solve(P(n));
            end.BlackCaptures.Should().BeGreaterThan(start.BlackCaptures, P(n).Name);
        }

        Solve(P(4)).BlackCaptures.Should().Be(1, "double atari wins one stone");
        Solve(P(3)).BlackCaptures.Should().BeGreaterThanOrEqualTo(4, "the snapback takes the whole group");

        // The other answer of the snapback (atari from outside) is also right.
        var direct = new TsumegoAttempt(P(3));
        direct.Play(new Point(3, 0)).Should().Be(TsumegoOutcome.Solved);

        // Life and death: the vital point, nothing else.
        foreach (int n in new[] { 5, 6, 7, 8 })
        {
            Solve(P(n));
            var other = new TsumegoAttempt(P(n));
            Point wrong = n is 5 or 6 ? new Point(0, 0) : new Point(1, 0);
            other.Play(wrong).Should().Be(TsumegoOutcome.Wrong, $"{P(n).Name}: only the vital point works");
        }
    }

    [AvaloniaFact]
    public void Right_answers_move_a_problem_up_and_next_brings_back_what_is_due()
    {
        var time = new ManualTime();
        var library = new TsumegoLibrary(_dir);
        var vm = new TsumegoViewModel(library, time: time);
        TsumegoProblem first = vm.Current!;
        first.Name.Should().Be("Tsumego.P1");

        vm.PlayCommand.Execute(new Point(4, 5));
        vm.IsSolved.Should().BeTrue();
        vm.StatusText.Should().Be(Tr.T("Tsumego.Solved"));
        library.Cards[first.Id].Box.Should().Be(1);

        vm.NextCommand.Execute(null);
        vm.Current!.Name.Should().Be("Tsumego.P2");
        vm.PlayCommand.Execute(new Point(8, 8));
        vm.IsWrong.Should().BeTrue();
        library.Cards[vm.Current.Id].Box.Should().Be(0);

        // A day later the first one is due again and comes before the new ones.
        time.Now += TimeSpan.FromDays(2);
        vm.NextCommand.Execute(null);
        vm.Current.Name.Should().BeOneOf("Tsumego.P1", "Tsumego.P2");

        // Progress survives a restart.
        new TsumegoLibrary(_dir).Cards.Should().ContainKey(first.Id);
    }

    [AvaloniaFact]
    public void The_answer_can_be_shown_and_then_counts_as_help()
    {
        var library = new TsumegoLibrary(_dir);
        var vm = new TsumegoViewModel(library, time: new ManualTime());
        vm.ShowAnswerCommand.Execute(null);
        vm.Markers.Should().ContainSingle().Which.Point.Should().Be(new Point(4, 5));
        vm.PlayCommand.Execute(new Point(4, 5));
        vm.IsSolved.Should().BeTrue();
        vm.StatusText.Should().Be(Tr.T("Tsumego.SolvedWithHelp"));
        library.Cards[vm.Current!.Id].Box.Should().Be(0, "shown answers come back soon");
    }

    [AvaloniaFact]
    public void A_collection_from_a_file_is_added()
    {
        var vm = new TsumegoViewModel(new TsumegoLibrary(_dir), time: new ManualTime());
        vm.AddSource(TsumegoLibrary.Parse("Mine", "(;SZ[9]AB[aa]AW[ba][ab];B[bb]C[RIGHT])(;SZ[9]C[no answer])"));
        vm.Rows.Should().HaveCount(9, "the second game has no answer");
        vm.Rows[^1].Source.Should().Be("Mine");
    }

    [AvaloniaFact]
    public void The_window_shows_the_board_and_the_list()
    {
        var vm = new TsumegoViewModel(new TsumegoLibrary(_dir), time: new ManualTime());
        var window = new TsumegoWindow { DataContext = vm };
        window.Show();
        window.FindDeep<GoBoardControl>("TsumegoBoard")!.Board!.Width.Should().Be(9);
        window.FindDeep<ListBox>("TsumegoList")!.ItemCount.Should().Be(8);
        window.FindDeep<TextBlock>("TsumegoInstructions")!.Text.Should().Be(Tr.T("Tsumego.Ask.Capture"));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        using (Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(dir, "tsumego.png"));
        }

        window.Close();
    }
}

[Collection("Language")]
public sealed class WelcomeTests
{
    [AvaloniaFact]
    public void The_tour_shows_once_and_ends_with_the_rules()
    {
        var settings = new TestSettings();
        var welcome = new WelcomeViewModel(settings);
        welcome.IsOpen.Should().BeTrue("first start");
        int rules = 0;
        welcome.RulesRequested += (_, _) => rules++;

        while (!welcome.IsLast)
        {
            welcome.Title.Should().NotStartWith("Welcome.");
            welcome.Text.Should().NotStartWith("Welcome.");
            welcome.NextCommand.Execute(null);
        }

        welcome.LearnRulesCommand.Execute(null);
        rules.Should().Be(1);
        welcome.IsOpen.Should().BeFalse();
        settings.Current.WelcomeSeen.Should().BeTrue();
        new WelcomeViewModel(settings).IsOpen.Should().BeFalse("seen once");
    }

    [AvaloniaFact]
    public void The_rules_lessons_are_solved_by_their_answers()
    {
        TsumegoSource rules = TsumegoLibrary.Rules();
        rules.Problems.Should().HaveCount(4);
        foreach (TsumegoProblem p in rules.Problems)
        {
            Tr.Has(p.Name).Should().BeTrue();
            var attempt = new TsumegoAttempt(p);
            attempt.Play(attempt.Hint!.Value).Should().Be(TsumegoOutcome.Solved, p.Name);
            Tr.Has(attempt.Comment!).Should().BeTrue("the explanation is translated");
        }

        var vm = new TsumegoViewModel(new TsumegoLibrary(), time: new ManualTime());
        vm.ShowRules();
        vm.Current!.Name.Should().Be("Rules.L1");
        vm.Rows[0].Source.Should().Be(Tr.T("Rules.Source"));
        vm.PlayCommand.Execute(vm.Current.Start.AllPoints.First(q => new TsumegoAttempt(vm.Current).Hint == q));
        vm.StatusText.Should().Contain(Tr.T("Rules.L1.Right"));
    }

    [AvaloniaFact]
    public void The_main_window_shows_the_tour_card()
    {
        var settings = new TestSettings();
        var vm = new MainWindowViewModel(new GameViewModel(), settings: settings, welcome: new WelcomeViewModel(settings));
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        window.FindDeep<Border>("WelcomeCard")!.IsVisible.Should().BeTrue();
        window.FindDeep<Button>("WelcomeCardNext")!.Command!.Execute(null);
        vm.Welcome.Step.Should().Be(1);
        vm.Welcome.CloseCommand.Execute(null);
        window.FindDeep<Border>("WelcomeCard")!.IsVisible.Should().BeFalse();
        window.Close();
    }
}
