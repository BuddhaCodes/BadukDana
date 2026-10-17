using Avalonia.Headless.XUnit;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Searching the played games: player, dates, result and a pattern from the board.</summary>
public sealed class LibrarySearchTests
{
    private static GameTree Game(string black, string white, string? result, string date, params (int X, int Y)[] moves)
    {
        GameTree tree = GameTree.Create(19, RuleSet.Japanese);
        tree.Info.BlackPlayer = black;
        tree.Info.WhitePlayer = white;
        tree.Info.Result = result;
        tree.Info.Date = date;
        var cursor = new GameCursor(tree);
        foreach ((int x, int y) in moves)
        {
            cursor.Play(new Point(x, y));
        }

        // Fill up to the library's minimum with far-away moves on the other side.
        for (int i = 0; cursor.MoveNumber < ReplayStore.MinMoves; i++)
        {
            cursor.Play(new Point(10 + (i % 8), 12 + (i / 8)));
        }

        return tree;
    }

    [AvaloniaFact]
    public async Task Filters_combine_and_the_pattern_is_found_in_any_corner()
    {
        string root = Directory.CreateTempSubdirectory("hoshi-search").FullName;
        try
        {
            var store = new ReplayStore(dataDirectory: root);
            // Ana's 3-4 approach top left; Luis's same shape in the bottom right, colours swapped; Marta's is elsewhere.
            store.Save(Game("Ana", "Luis", "B+R", "2026-10-02", (3, 2), (2, 4)), "a", "local");
            store.Save(Game("Marta", "Luis", "W+3.5", "2026-09-20", (15, 16), (16, 14)), "b", "local");
            store.Save(Game("Marta", "Pablo", null, "2026-10-05", (9, 9), (3, 3)), "c", "ogs");
            var list = new ReplaysViewModel(store);
            list.Rows.Should().HaveCount(3);

            list.PlayerFilter = "luis";
            list.Rows.Should().HaveCount(2);
            list.SelectedResult = list.Results.Single(r => r.Filter == ResultFilter.WhiteWins);
            list.Rows.Should().ContainSingle().Which.Entry.Black.Should().Be("Marta");
            list.ClearFiltersCommand.Execute(null);
            list.Rows.Should().HaveCount(3);

            // Dates: ReplayStore keeps when the game was saved, so filter on today.
            list.FromDateValue = DateTime.Today.AddDays(1);
            list.Rows.Should().BeEmpty();
            list.EmptyText.Should().Be(Hoshi.Core.Localization.Tr.T("Replays.NoMatch"));
            list.FromDateValue = null;

            var source = GameTree.Create(19, RuleSet.Japanese);
            var cursor = new GameCursor(source);
            cursor.Play(new Point(3, 2));
            cursor.Play(new Point(2, 4));
            list.PatternSource = () => BoardPattern.FromRegion(cursor.Board, 0, 0, 6, 6);
            list.UsePatternCommand.Execute(null);
            list.HasPattern.Should().BeTrue();
            await list.Searching;

            list.Rows.Select(r => (r.Entry.Black, r.MatchMove)).Should().BeEquivalentTo(new[] { ("Ana", (int?)2), ("Marta", (int?)2) });
            list.Rows[0].Details.Should().Contain(Hoshi.Core.Localization.Tr.F("Replays.PatternAt", 2));
            list.IsSearching.Should().BeFalse();

            list.ClearPatternCommand.Execute(null);
            list.Rows.Should().HaveCount(3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_main_window_takes_the_pattern_from_the_study_area_or_the_last_moves_corner_and_opens_at_the_match()
    {
        var game = new GameViewModel();
        var main = new MainWindowViewModel(game, settings: new TestSettings());
        main.SearchPattern().Should().BeNull("an empty board has no pattern");

        game.PlayCommand.Execute(new Point(15, 3));
        BoardPattern corner = main.SearchPattern()!;
        (corner.X, corner.Y, corner.Width, corner.Height).Should().Be((10, 0, 9, 9));

        Hoshi.Sgf.Study.StudyStore.Write(game.CurrentNode, new Hoshi.Sgf.Study.NodeStudy([], [new Hoshi.Sgf.Study.StudyDrawing("x", Hoshi.Sgf.Study.DrawingKind.Area, "me", Hoshi.Sgf.Study.StudyColor.Red, [new Point(17, 5), new Point(13, 1)])]));
        BoardPattern area = main.SearchPattern()!;
        (area.X, area.Y, area.Width, area.Height).Should().Be((13, 1, 5, 5));
    }
}
