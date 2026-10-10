using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services.Study;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

[Collection("Language")]
public sealed class StudyUiTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 20, 15, 0, TimeSpan.Zero);

    private static readonly (int X, int Y)[] Game =
        [(15, 3), (3, 15), (16, 15), (3, 3), (14, 16), (2, 5), (5, 2), (16, 9), (9, 15), (13, 2), (15, 13), (2, 13), (9, 3), (11, 15), (16, 5), (14, 3), (15, 2)];

    /// <summary>A reviewed game: pins by two people on a few moves, with arrows, an area and a "what if" line.</summary>
    private static void Study(GameViewModel game, bool english)
    {
        game.Tree.Info.BlackPlayer = "Ana";
        game.Tree.Info.WhitePlayer = "Ben";
        game.Tree.Info.GameName = english ? "Club review" : "Repaso del club";
        foreach ((int x, int y) in Game)
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        game.Tree.Info.BlackPlayer = "Ana";
        game.Tree.Info.WhitePlayer = "Ben";
        GameNode Node(int n)
        {
            GameNode node = game.Tree.Root;
            for (int i = 0; i < n; i++)
            {
                node = node.Children[0];
            }

            return node;
        }

        string T(string en, string es) => english ? en : es;
        StudyStore.Write(Node(4), new NodeStudy([new StudyPin("p1", PinCategory.Joseki, "Ana", T0, T("Standard, fine for both.", "Estándar, bien para ambos."), [])], []));
        StudyStore.Write(Node(8), new NodeStudy(
            [new StudyPin("p2", PinCategory.Question, "Ana", T0, T("Was the approach urgent?", "¿Era urgente la aproximación?"), [new StudyReply("Ben", T0.AddHours(2), T("Yes: the lower side is big.", "Sí: el lado de abajo es grande."))])], []));
        StudyStore.Write(Node(12), new NodeStudy([new StudyPin("p3", PinCategory.KeyMoment, "Ben", T0, string.Empty, [])], []));
        StudyStore.Write(Node(14), new NodeStudy(
            [
                new StudyPin("p4", PinCategory.Mistake, "Ana", T0.AddMinutes(4), T("Too slow. The extension at R11 kept the pressure.", "Demasiado lenta. La extensión en R11 mantenía la presión."),
                    [new StudyReply("Ben", T0.AddHours(3), T("Agreed, and White gets sente.", "De acuerdo, y Blanco toma sente."))]),
                new StudyPin("p5", PinCategory.Lesson, "Ana", T0.AddMinutes(5), T("Attack from the side you want to build.", "Ataca desde el lado que quieres construir."), []),
            ],
            [
                new StudyDrawing("d1", DrawingKind.Area, "Ana", StudyColor.Blue, [new Point(13, 10), new Point(18, 16)]),
                new StudyDrawing("d2", DrawingKind.Arrow, "Ana", StudyColor.Red, [new Point(16, 9), new Point(16, 12)]),
                new StudyDrawing("d3", DrawingKind.Sequence, "Ben", StudyColor.Purple, [new Point(16, 11), new Point(14, 9), new Point(17, 13)], "W"),
                new StudyDrawing("d4", DrawingKind.Mark, "Ben", StudyColor.Gold, [new Point(9, 15)]),
                new StudyDrawing("d5", DrawingKind.Label, "Ana", StudyColor.Green, [new Point(12, 13)], "A"),
            ]));
        StudyStore.Write(Node(17), new NodeStudy([new StudyPin("p6", PinCategory.GoodMove, "Ben", T0, T("Nice hane.", "Buen hane."), [])], []));
        game.GoToNodeCommand.Execute(Node(14));
        game.StudyEdited();
    }

    [AvaloniaFact]
    public void S_opens_the_study_panel_over_the_comment_box_and_the_board_draws_the_study()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1280, Height = 1500 };
        window.Show();
        Study(vm.Game, english: false);

        window.FindControl<GoBoardControl>("Board")!.Focus();
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
        vm.Study.IsActive.Should().BeTrue();
        window.UpdateLayout();
        window.FindControl<Border>("StudyPanel")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<TextBox>("CommentBox")!.IsEffectivelyVisible.Should().BeFalse();
        window.FindControl<GoBoardControl>("Board")!.StudyDrawings.Should().HaveCount(5);
        window.FindControl<TextBlock>("StudyMoveTitle")!.Text.Should().Be("Jugada 14 · Blancas M4");
        window.FindControl<StudyTimeline>("StudyTimeline")!.Marks.Should().HaveCount(6);

        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        // The whole panel, for a look at the Spanish texts (the window is made tall enough for everything).
        window.FindControl<Border>("StudyPanel")!.Height = 1080;
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(dir, "study-es.png"));
        }

        // Typing an "s" in a note does not close the panel.
        window.FindControl<TextBox>("StudyNewPin")!.Focus();
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
        window.KeyTextInput("s");
        vm.Study.IsActive.Should().BeTrue();
        vm.Study.NewPinText.Should().Be("s");

        window.FindControl<Button>("StudyClose")!.Command!.Execute(null);
        window.UpdateLayout();
        window.FindControl<TextBox>("CommentBox")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<GoBoardControl>("Board")!.StudyDrawings.Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_on_the_board_draws_an_arrow_and_a_tap_still_plays()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        GoBoardControl board = window.FindControl<GoBoardControl>("Board")!;
        Avalonia.Point At(int x, int y) => board.TranslatePoint(board.CurrentGeometry!.Value.Center(new Point(x, y)), window)!.Value;

        // Without a drawing tool a slightly sliding click still plays.
        window.MouseDown(At(3, 3), MouseButton.Left);
        window.MouseMove(At(4, 3));
        window.MouseUp(At(4, 3), MouseButton.Left);
        vm.Game.MoveNumber.Should().Be(1);

        vm.Study.IsActive = true;
        vm.Study.SelectToolCommand.Execute(StudyTool.Arrow);
        board.DragPreview.Should().Be(DragPreview.Arrow);
        window.MouseDown(At(10, 10), MouseButton.Left);
        window.MouseMove(At(12, 12), RawInputModifiers.LeftMouseButton);
        window.MouseUp(At(12, 12), MouseButton.Left);
        vm.Game.MoveNumber.Should().Be(1);
        board.StudyDrawings.Should().ContainSingle().Which.Points.Should().Equal(new Point(10, 10), new Point(12, 12));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Study_screenshots_are_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        try
        {
            ThemeService.Apply(HoshiThemes.InkAndGold, animations: false, Application.Current!.Resources);
            var vm = new MainWindowViewModel(new GameViewModel());
            Study(vm.Game, english: true);
            vm.Study.Author = "Ana";
            vm.Study.IsActive = true;
            var window = new MainWindow(vm) { Width = 1400, Height = 880 };
            window.Show();
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "study.png"));
            }

            GameNode mistake = vm.Game.CurrentNode;

            // The quiz: question 4 (the mistake), answered wrong.
            vm.Study.StartQuizCommand.Execute(null);
            vm.Study.NextQuestionCommand.Execute(null);
            vm.Study.RevealAnswerCommand.Execute(null);
            vm.Study.MissedCommand.Execute(null);
            vm.Study.NextQuestionCommand.Execute(null);
            vm.Study.NextQuestionCommand.Execute(null);
            vm.Study.Question!.Pin.Category.Should().Be(PinCategory.Mistake);
            vm.Study.BoardClickCommand.Execute(new Point(16, 12));
            await Task.Delay(60);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "study-quiz.png"));
            }

            File.WriteAllText(Path.Combine(dir, "study-report.html"), StudyReport.Html(
                vm.Game.Tree,
                new Dictionary<GameNode, StudyVerdict>
                {
                    [vm.Game.Tree.Root.Children[0].Children[0].Children[0].Children[0]] = new(Engines.KataGo.MoveQuality.Best, 0, null),
                    [mistake] = new(Engines.KataGo.MoveQuality.Inaccuracy, 2.3, new Point(16, 11)),
                },
                T0));
            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }
}
