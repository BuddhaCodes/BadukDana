using FluentAssertions;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Ogs.Reviews;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Tests;

/// <summary>Teacher mode through OGS review rooms (fake OGS: never the real server in CI).</summary>
public sealed class TeachingTests
{
    private const string OgsSgf = "(;GM[1]FF[4]SZ[9]PC[OGS: https://online-go.com/game/4242]PB[ana]PW[ben];B[cc];W[gg];B[gc];W[cg])";

    private static (GameViewModel Game, FakeOgsClient Ogs, TeachingViewModel Teaching) Setup()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        var ogs = new FakeOgsClient();
        var teaching = new TeachingViewModel(game, ogs, () => Hoshi.Ogs.OgsLinks.TryGameId(game.Tree.Root.GetValue("PC")));
        return (game, ogs, teaching);
    }

    [Fact]
    public void A_line_is_located_from_where_it_leaves_the_game()
    {
        GameTree tree = SgfParser.Parse(OgsSgf);
        GameNode second = tree.Root.Children[0].Children[0];
        TeachingViewModel.Locate(second, 9).From.Should().Be(2);
        TeachingViewModel.Locate(second, 9).Moves.Should().BeEmpty();

        GameNode variation = second.AddChild();
        variation.SetValue("B", "ee");
        GameNode answer = variation.AddChild();
        answer.SetValue("W", "");
        (int from, IReadOnlyList<Point?> moves) = TeachingViewModel.Locate(answer, 9);
        from.Should().Be(2);
        moves.Should().Equal(new Point(4, 4), null);
        OgsReviewSession.Encode(moves).Should().Be("ee..");
        OgsReviewSession.Decode("ee..!1ccgg").Should().Equal(new Point(4, 4), null, new Point(6, 6));
    }

    [Fact]
    public async Task The_teacher_opens_a_room_for_the_OGS_game_and_shows_every_move_with_its_notes_and_marks()
    {
        (GameViewModel game, FakeOgsClient ogs, TeachingViewModel teaching) = Setup();
        teaching.CanTeach.Should().BeFalse("not signed in");
        ogs.SignInNow();
        teaching.CanTeach.Should().BeFalse("no OGS game on the board");
        await game.OpenSgfTextAsync(OgsSgf, "OGS #4242", confirm: false);
        teaching.CanTeach.Should().BeTrue();

        await teaching.StartTeachingCommand.ExecuteAsync(null);
        ogs.ReviewsCreated.Should().Equal(4242);
        FakeReviewRoom room = ogs.Rooms[9001];
        room.Connected.Should().BeTrue();
        teaching.RoomLink.Should().Be("https://beta.online-go.com/review/9001");

        game.GoToNodeCommand.Execute(game.Tree.Root.Children[0].Children[0]);
        var study = new StudyViewModel(game) { IsActive = true };
        study.AddPin(PinCategory.Mistake, "Too slow");
        StudyStore.Write(game.CurrentNode, StudyStore.Read(game.CurrentNode) with
        {
            Drawings = [new StudyDrawing("m", DrawingKind.Mark, "Ana", StudyColor.Red, [new Point(4, 4)]), new StudyDrawing("l", DrawingKind.Label, "Ana", StudyColor.Gold, [new Point(5, 5)], "A")],
        });
        game.StudyEdited();
        game.PlayCommand.Execute(new Point(4, 4)); // a variation

        room.Shown.Select(s => (s.From, s.Moves)).Should().ContainInOrder((0, ""), (2, ""), (2, "ee"));
        (int From, string Moves, string Text, IReadOnlyDictionary<string, IReadOnlyList<Point>> Marks) atTwo = room.Shown.Last(s => s.From == 2 && s.Moves.Length == 0);
        atTwo.Text.Should().Contain("Too slow");
        atTwo.Marks["circle"].Should().Equal(new Point(4, 4));
        atTwo.Marks["A"].Should().Equal(new Point(5, 5));

        teaching.LeaveCommand.Execute(null);
        room.Disposed.Should().BeTrue();
        int shown = room.Shown.Count;
        game.GoFirstCommand.Execute(null);
        room.Shown.Should().HaveCount(shown, "nothing is sent after leaving");
    }

    [Fact]
    public async Task A_student_follows_the_teachers_room()
    {
        (GameViewModel game, FakeOgsClient ogs, TeachingViewModel teaching) = Setup();
        ogs.SignInNow();
        ogs.Sgf[4242] = OgsSgf;
        teaching.JoinText = "https://online-go.com/review/777";
        teaching.FollowCommand.CanExecute(null).Should().BeTrue();
        teaching.FollowCommand.Execute(null);
        FakeReviewRoom room = ogs.Rooms[777];

        room.RaiseFullState(new OgsReviewMessage { GameId = 4242, OwnerId = 5, ControllerId = 5 }, new OgsReviewMessage { From = 1, Moves = [], Text = "Look here" });
        await Task.Delay(50);
        game.Tree.Root.GetValue("PC").Should().Contain("4242");
        game.MoveNumber.Should().Be(1);
        teaching.Status.Should().Be("Look here");

        room.RaiseAppended(new OgsReviewMessage { From = 2, Moves = [new Point(4, 4), null] });
        game.MoveNumber.Should().Be(4);
        game.CurrentNode.Parent!.GetMove(9)!.Value.Point.Should().Be(new Point(4, 4));
        teaching.IsFollowing.Should().BeTrue();
    }
}
