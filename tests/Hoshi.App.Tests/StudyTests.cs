using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Ogs;
using Hoshi.Ogs.Games;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Tests;

internal sealed class StudyFiles : IFileDialogService
{
    public string? OpenPath { get; set; }

    public string? SavePath { get; set; }

    public string? HtmlPath { get; set; }

    public Task<string?> PickSgfToOpenAsync() => Task.FromResult(OpenPath);

    public Task<string?> PickSgfToSaveAsync(string suggestedName) => Task.FromResult(SavePath);

    public Task<string?> PickHtmlToSaveAsync(string suggestedName) => Task.FromResult(HtmlPath);
}

internal sealed class StudyDialogs : IDialogService
{
    public bool Answer { get; set; } = true;

    public List<string> Asked { get; } = [];

    public Task<bool> EditGameInfoAsync(GameInfoViewModel info) => Task.FromResult(false);

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Asked.Add(message);
        return Task.FromResult(Answer);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        Asked.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class StudyViewModelTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 18, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-study-" + Guid.NewGuid().ToString("N"));
    private readonly GameViewModel _game = new(9, RuleSet.Japanese);
    private readonly TestSettings _settings = new();
    private readonly StudyFiles _files = new();
    private readonly StudyDialogs _dialogs = new();
    private readonly FakeBrowser _browser = new();
    private readonly StudyViewModel _study;

    public StudyViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _study = new StudyViewModel(_game, new AnalysisViewModel(_game, ui: new ImmediateDispatcher()), _settings, _files, _dialogs, _browser, new FixedTime(T0));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Play(params (int X, int Y)[] moves)
    {
        foreach ((int x, int y) in moves)
        {
            _game.PlayCommand.Execute(new Point(x, y));
        }
    }

    [Fact]
    public void Drawing_tools_write_to_the_current_move_and_never_play()
    {
        Play((2, 2), (6, 6));
        _game.IsDirty = false;
        _study.ToggleCommand.Execute(null);
        _study.IsActive.Should().BeTrue();

        _study.SelectToolCommand.Execute(StudyTool.Mark);
        _study.BoardClickCommand.Execute(new Point(4, 4));
        _game.MoveNumber.Should().Be(2, "a drawing tool does not play");
        _game.Board[new Point(4, 4)].Should().Be(Stone.Empty);
        _study.Drawings.Should().ContainSingle().Which.Kind.Should().Be(DrawingKind.Mark);
        _study.BoardDrawings.Should().HaveCount(1);
        _game.IsDirty.Should().BeTrue();
        _study.BoardClickCommand.Execute(new Point(4, 4));
        _study.Drawings.Should().BeEmpty("a second click takes the circle away");

        _study.SelectToolCommand.Execute(StudyTool.Label);
        _study.BoardClickCommand.Execute(new Point(1, 1));
        _study.BoardClickCommand.Execute(new Point(7, 1));
        _study.Drawings.Select(d => d.Text).Should().Equal("A", "B");

        _study.SelectToolCommand.Execute(StudyTool.Arrow);
        _study.DragPreview.Should().Be(DragPreview.Arrow);
        _study.SelectColorCommand.Execute(StudyColor.Red);
        _study.BoardDragCommand.Execute(new BoardDrag(new Point(2, 2), new Point(5, 5)));
        _study.Drawings.Last().Should().Match<StudyDrawing>(d => d.Kind == DrawingKind.Arrow && d.Color == StudyColor.Red && d.Points[1] == new Point(5, 5));

        _study.SelectToolCommand.Execute(StudyTool.Erase);
        _study.BoardClickCommand.Execute(new Point(5, 5));
        _study.Drawings.Should().HaveCount(2, "erasing touches the arrow's end");

        // The drawings belong to the move: elsewhere the board is clean, back here they return.
        _game.GoBackCommand.Execute(null);
        _study.BoardDrawings.Should().BeEmpty();
        _game.GoForwardCommand.Execute(null);
        _study.BoardDrawings.Should().HaveCount(2);

        // Closing the study hides them; "Play" clicks play again.
        _study.SelectToolCommand.Execute(StudyTool.Note);
        _study.BoardClickCommand.Execute(new Point(4, 4));
        _game.MoveNumber.Should().Be(3);
        _study.ToggleCommand.Execute(null);
        _study.BoardDrawings.Should().BeEmpty();
    }

    [Fact]
    public void What_if_sequences_alternate_colours_and_click_back()
    {
        Play((2, 2));
        _study.IsActive = true;
        _study.SelectToolCommand.Execute(StudyTool.Sequence);
        _game.HoverPoint = new Point(5, 5);
        _study.GhostStone.Should().Be(Stone.White, "White is to play");

        _study.BoardClickCommand.Execute(new Point(5, 5));
        _study.BoardClickCommand.Execute(new Point(6, 5));
        _study.BoardClickCommand.Execute(new Point(2, 2)); // occupied: ignored
        _study.BoardClickCommand.Execute(new Point(6, 6));
        StudyDrawing line = _study.Drawings.Should().ContainSingle().Subject;
        line.Points.Should().Equal(new Point(5, 5), new Point(6, 5), new Point(6, 6));
        line.FirstColor.Should().Be(Stone.White);
        _game.HoverPoint = new Point(0, 0);
        _study.GhostStone.Should().Be(Stone.Black, "the fourth stone is Black's");

        _study.BoardClickCommand.Execute(new Point(6, 6));
        _study.Drawings.Single().Points.Should().HaveCount(2, "clicking the last stone takes it back");

        _study.NewSequenceCommand.Execute(null);
        _study.BoardClickCommand.Execute(new Point(0, 8));
        _study.Drawings.Should().HaveCount(2);
        _game.MoveNumber.Should().Be(1);
    }

    [Fact]
    public void Pins_replies_and_the_timeline()
    {
        _study.Author = "Ana";
        _settings.Current.StudyAuthor.Should().Be("Ana");
        Play((2, 2), (6, 6), (6, 2), (2, 6));
        _game.GoToNodeCommand.Execute(Node(2));
        _study.AddPin(PinCategory.Mistake, "Too slow");
        _game.GoToNodeCommand.Execute(Node(4));
        _study.AddPin(PinCategory.GoodMove, string.Empty);

        _study.Pins.Should().ContainSingle().Which.CategoryName.Should().Be("Buena jugada");
        _study.Timeline.Select(t => t.Move).Should().Equal(2, 4);
        _study.TimelineTotal.Should().Be(4);
        _study.TimelineCurrent.Should().Be(4);
        _study.TimelineText.Should().Be("2 jugadas estudiadas");

        _study.PreviousPinCommand.Execute(null);
        _game.MoveNumber.Should().Be(2);
        StudyPinRow row = _study.Pins.Single();
        row.Author.Should().Be("Ana");
        row.Text.Should().Be("Too slow");
        row.ReplyText = "Tenuki was bigger";
        row.ReplyCommand.Execute(null);
        _study.Pins.Single().Replies.Should().ContainSingle().Which.Text.Should().Be("Tenuki was bigger");

        _study.Filter = _study.Filters.Single(f => f.Category == PinCategory.GoodMove);
        _study.Timeline.Should().ContainSingle().Which.Move.Should().Be(4);
        _game.GoToNodeCommand.Execute(_game.Tree.Root);
        _study.NextPinCommand.Execute(null);
        _game.MoveNumber.Should().Be(4, "the filter skips the mistake");
        _study.GoToMarkCommand.Execute(_study.Timeline[0]);
        _game.MoveNumber.Should().Be(4);

        _study.Pins.Single().DeleteCommand.Execute(null);
        StudyStore.Read(Node(4)).IsEmpty.Should().BeTrue();
        _study.Timeline.Should().BeEmpty();
        _game.Tree.Root.Children[0].Children[0].HasProperty(StudyStore.Property).Should().BeTrue();
    }

    [Fact]
    public async Task Studies_are_saved_merged_and_opened()
    {
        Play((2, 2), (6, 6), (6, 2));
        _study.Author = "Ana";
        _game.GoToNodeCommand.Execute(Node(2));
        _study.AddPin(PinCategory.Question, "Why here?");

        // Save: an SGF other programs read (comment + markup) that Hoshi reopens as a study.
        _files.SavePath = Path.Combine(_dir, "mine.sgf");
        await _study.SaveStudyCommand.ExecuteAsync(null);
        string text = File.ReadAllText(_files.SavePath);
        text.Should().Contain("HS[").And.Contain("Why here?").And.Contain(StudyStore.MirrorMarker);

        // A friend replies and adds a pin on a move we don't have.
        GameTree friend = SgfParser.Parse(text);
        StudyStore.StripMirror(friend);
        GameNode q = friend.Root.Children[0].Children[0];
        NodeStudy theirs = StudyStore.Read(q);
        StudyStore.Write(q, theirs with { Pins = [theirs.Pins[0] with { Replies = [new StudyReply("Ben", T0, "To stop the shimari")] }] });
        GameNode extra = friend.Root.Children[0].Children[0].Children[0].AddChild();
        extra.SetValue("W", "hh");
        StudyStore.Write(extra, new NodeStudy([new StudyPin("ben1", PinCategory.Idea, "Ben", T0, "Try this", [])], []));
        string friendPath = Path.Combine(_dir, "friend.sgf");
        File.WriteAllText(friendPath, StudyStore.WriteWithMirror(friend));

        _files.OpenPath = friendPath;
        await _study.OpenStudyCommand.ExecuteAsync(null);
        _game.StatusText.Should().Contain("2 notas");
        StudyStore.Read(Node(2)).Pins.Single().Replies.Should().ContainSingle().Which.Author.Should().Be("Ben");
        StudyStore.Authors(_game.Tree).Should().Equal("Ana", "Ben");
        _game.Tree.Root.Children[0].Children[0].Children[0].Children.Should().ContainSingle("Ben's move is added");
        _dialogs.Asked.Should().BeEmpty();

        // From an empty board, a study opens as the game itself, without its mirror text.
        var other = new GameViewModel(9, RuleSet.Japanese);
        var study = new StudyViewModel(other, null, _settings, _files, _dialogs);
        await study.OpenStudyFileAsync(friendPath);
        other.MainLineMoveCount.Should().Be(4);
        other.Tree.Root.Children[0].Children[0].Comment.Should().BeNull("the copy for other programs is removed");
        study.IsActive.Should().BeTrue();
        StudyStore.All(other.Tree).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_study_of_another_game_asks_before_replacing_the_board()
    {
        Play((2, 2), (6, 6));
        var tree = GameTree.Create(9);
        GameNode n = tree.Root.AddChild();
        n.SetValue("B", "ee");
        StudyStore.Write(n, new NodeStudy([new StudyPin("x", PinCategory.Lesson, "Ben", T0, "Center first", [])], []));
        string path = Path.Combine(_dir, "other.sgf");
        File.WriteAllText(path, StudyStore.WriteWithMirror(tree));

        _dialogs.Answer = false;
        await _study.OpenStudyFileAsync(path);
        _dialogs.Asked.Should().ContainSingle();
        _game.MainLineMoveCount.Should().Be(2, "declined: the game on the board stays");
        _game.StatusText.Should().Be("Ese estudio es de otra partida.");
        StudyStore.All(_game.Tree).Should().BeEmpty();
        StudyViewModel.SameGame(_game.Tree, tree).Should().BeFalse();
    }

    [Fact]
    public void The_quiz_asks_from_the_pins_and_scores_the_answers()
    {
        Play((2, 2), (6, 6), (6, 2), (2, 6), (4, 4));
        _study.IsActive = true;
        _game.GoToNodeCommand.Execute(Node(3));
        _study.AddPin(PinCategory.GoodMove, "The 3-3 approach");
        _game.GoToNodeCommand.Execute(Node(4));
        _study.AddPin(PinCategory.Mistake, "Should have taken the centre");
        _study.SelectToolCommand.Execute(StudyTool.Sequence);
        _study.BoardClickCommand.Execute(new Point(4, 4)); // White's better move, drawn on the mistake
        _game.GoToNodeCommand.Execute(Node(5));
        _study.AddPin(PinCategory.Question, "Is this big?");
        _game.GoToNodeCommand.Execute(Node(1));
        _study.AddPin(PinCategory.Lesson, "Corners first"); // lessons make no question

        _study.StartQuizCommand.Execute(null);
        _study.IsQuizActive.Should().BeTrue();
        _study.QuizProgress.Should().Be("Pregunta 1 de 3");
        _game.MoveNumber.Should().Be(2, "the position before the pinned move");
        _study.QuizPrompt.Should().Be("Juegan Negras. Haz clic en tu jugada.");
        _game.HoverPoint = new Point(0, 0);
        _study.GhostStone.Should().Be(Stone.Black);

        _study.BoardClickCommand.Execute(new Point(6, 2));
        _game.MoveNumber.Should().Be(2, "answering does not play");
        _study.QuizResult.Should().Be("¡Correcto!");
        _study.QuizScore.Should().Be("1 / 1");
        _study.QuizNote.Should().Contain("The 3-3 approach");
        _study.CanGoNext.Should().BeTrue();

        _study.NextQuestionCommand.Execute(null);
        _study.Question!.Answer.Should().Be(new Point(4, 4), "the first stone of the 'what if' line drawn on the mistake");
        _study.BoardClickCommand.Execute(new Point(2, 6)); // the mistake itself
        _study.QuizResult.Should().Be("No exactamente: E5 (verde).");
        _study.QuizOverlay.Should().Contain(d => d.Kind == DrawingKind.Mark && d.Color == StudyColor.Green && d.Points[0] == new Point(4, 4));
        _study.BoardDrawings.Should().Contain(d => d.Kind == DrawingKind.Label && d.Text == "!");

        _study.NextQuestionCommand.Execute(null);
        _study.IsQuizScored.Should().BeFalse("a question has no known answer");
        _study.RevealAnswerCommand.Execute(null);
        _study.CanSelfGrade.Should().BeFalse("revealing without a guess is a miss");
        _study.QuizScore.Should().Be("1 / 3");

        _study.NextQuestionCommand.Execute(null);
        _study.IsQuizFinished.Should().BeTrue();
        _study.QuizResult.Should().Be("Terminado: 1 de 3 acertadas.");
        _study.EndQuizCommand.Execute(null);
        _study.IsQuizActive.Should().BeFalse();
    }

    [Fact]
    public void Unscored_questions_are_graded_by_the_player()
    {
        Play((2, 2), (6, 6));
        _game.GoToNodeCommand.Execute(Node(2));
        _study.AddPin(PinCategory.Question, "Why not 3-3?");
        _study.StartQuizCommand.Execute(null);
        _study.BoardClickCommand.Execute(new Point(2, 6));
        _study.QuizResult.Should().Be("Compara con la nota: ¿la encontraste?");
        _study.CanSelfGrade.Should().BeTrue();
        _study.CanGoNext.Should().BeFalse();
        _study.GotItCommand.Execute(null);
        _study.QuizScore.Should().Be("1 / 1");
        _study.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public async Task The_report_is_a_self_contained_page_with_the_pins_and_KataGos_verdicts()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(4, 4) };
        var analysis = new AnalysisViewModel(_game, engine, new ImmediateDispatcher());
        var study = new StudyViewModel(_game, analysis, _settings, _files, _dialogs, _browser, new FixedTime(T0));
        _game.Tree.Info.BlackPlayer = "Ana <b>";
        Play((2, 2), (4, 4), (6, 6));
        study.IsActive = true;
        _game.GoToNodeCommand.Execute(Node(1));
        study.AddPin(PinCategory.Mistake, "Too <slow>");
        study.SelectToolCommand.Execute(StudyTool.Arrow);
        study.BoardDragCommand.Execute(new BoardDrag(new Point(2, 2), new Point(4, 4)));
        _game.GoToNodeCommand.Execute(Node(3));
        study.AddPin(PinCategory.Lesson, "Take the centre");

        _files.HtmlPath = Path.Combine(_dir, "report.html");
        await study.ReportCommand.ExecuteAsync(null);
        string html = File.ReadAllText(_files.HtmlPath);
        html.Should().StartWith("<!doctype html>").And.Contain("Ana &lt;b&gt;").And.Contain("Too &lt;slow&gt;");
        html.Should().Contain("Error").And.Contain("Lecciones").And.Contain("Take the centre").And.Contain("<svg").And.Contain("<polygon");
        html.Should().Contain("KataGo:").And.Contain("mejor E5", "B C7 lost points against the best move E5");
        html.Should().NotContain("<script").And.NotContain("src=").And.NotContain("href=");
        _browser.Opened.Should().ContainSingle().Which.IsFile.Should().BeTrue();
    }

    [Fact]
    public void The_report_without_an_engine_still_has_every_moment()
    {
        GameTree tree = GameTree.Create(9);
        GameNode n = tree.Root.AddChild();
        n.SetValue("B", "cc");
        StudyStore.Write(n, new NodeStudy([], [new StudyDrawing("a", DrawingKind.Area, "Ana", StudyColor.Blue, [new Point(0, 0), new Point(3, 3)])]));
        string html = StudyReport.Html(tree, now: T0);
        html.Should().Contain("Solo dibujos.").And.Contain("Todavía no hay pins").And.Contain("Hecho con Hoshi · 2026-10-10").And.NotContain("KataGo:");
    }

    [Fact]
    public void During_a_live_OGS_game_the_study_never_sends_moves_and_survives_reloads()
    {
        var online = new FakeOnlineGame(70000001, 1001);
        var vm = new OnlineGameViewModel(online, _game, new ImmediateDispatcher());
        vm.Connect();
        OgsGameSnapshot Snapshot(params OgsGameMove[] moves) => new()
        {
            GameId = 70000001,
            Width = 9,
            Height = 9,
            Komi = 6.5,
            Black = new OgsUser(1001, "kuro_test", 25.4, false),
            White = new OgsUser(2002, "shiro_test", 27.2, false),
            Moves = moves,
            TimeControl = new OgsTimeControl { System = "byoyomi", MainTime = 600, PeriodTime = 30, Periods = 5 },
        };
        online.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2))));
        _study.IsLiveOnline.Should().BeTrue();
        _study.IsActive = true;

        _study.SelectToolCommand.Execute(StudyTool.Mark);
        _study.BoardClickCommand.Execute(new Point(6, 6));
        _study.SelectToolCommand.Execute(StudyTool.Sequence);
        _study.BoardClickCommand.Execute(new Point(6, 2));
        online.Sent.Should().BeEmpty("drawing tools never reach OGS");
        _study.AddPin(PinCategory.Idea, "Their group is thin");

        _study.StartQuizCommand.CanExecute(null).Should().BeFalse("the quiz would take the board during the game");
        _game.IsDirty.Should().BeFalse();

        // A reconnect sends the whole game again: the notes stay on their moves.
        online.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2)), new(3, Stone.Black, new Point(6, 6)), new(4, Stone.White, new Point(7, 7))));
        _game.MainLineMoveCount.Should().Be(4);
        NodeStudy kept = StudyStore.Read(_game.Tree.Root.Children[0].Children[0]);
        kept.Pins.Should().ContainSingle().Which.Text.Should().Be("Their group is thin");
        kept.Drawings.Should().HaveCount(2);

        // "Play" sends the click to OGS as before.
        _study.SelectToolCommand.Execute(StudyTool.Note);
        _study.BoardClickCommand.Execute(new Point(2, 6));
        online.Sent.Should().ContainSingle().Which.Should().Be("move cg");
    }

    private GameNode Node(int moveNumber)
    {
        GameNode n = _game.Tree.Root;
        for (int i = 0; i < moveNumber; i++)
        {
            n = n.Children[0];
        }

        return n;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
