using FluentAssertions;
using Hoshi.App.Services;
using Hoshi.App.Services.Backup;
using Hoshi.App.Services.Study;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Tests;

/// <summary>Spaced repetition of the notes pinned in the library's games.</summary>
public sealed class NoteReviewTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-notes-" + Guid.NewGuid().ToString("N"));
    private readonly ReplayStore _store;

    public NoteReviewTests()
    {
        _store = new ReplayStore(dataDirectory: _dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>A 12-move game with a good move pinned at move 3 and a lesson (no card) at move 1.</summary>
    private ReplayEntry SaveGame(string id, int shift, PinCategory category = PinCategory.GoodMove)
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        var study = new StudyViewModel(game);
        (int X, int Y)[] moves = [(2, 2), (6, 6), (6, 2), (2, 6), (4, 4), (4, 2), (2, 4), (6, 4), (4, 6), (1, 1), (7, 7), (1, 7)];
        foreach ((int x, int y) in moves)
        {
            game.PlayCommand.Execute(new Point((x + shift) % 9, y));
        }

        game.GoToNodeCommand.Execute(Node(game.Tree, 3));
        study.AddPin(category, "Approach " + id);
        game.GoToNodeCommand.Execute(Node(game.Tree, 1));
        study.AddPin(PinCategory.Lesson, "Corners first");
        return _store.Save(game.Tree, id, "local")!;
    }

    private static GameNode Node(GameTree tree, int moveNumber)
    {
        GameNode n = tree.Root;
        for (int i = 0; i < moveNumber; i++)
        {
            n = n.Children[0];
        }

        return n;
    }

    [Fact]
    public void Pinned_mistakes_good_moves_and_key_moments_make_cards_and_answers_space_them_out()
    {
        SaveGame("a", 0);
        SaveGame("b", 1, PinCategory.Mistake);
        var deck = new NoteDeck(_dir);

        IReadOnlyList<NoteReviewItem> all = NoteDeck.Collect(_store);
        all.Should().HaveCount(2, "lessons make no card");
        all.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        deck.Session(all, T0).Should().HaveCount(2, "never reviewed cards are new today");

        deck.Grade(all[0].Id, right: true, T0);
        deck.Grade(all[1].Id, right: false, T0);
        deck.Session(all, T0.AddMinutes(1)).Should().BeEmpty("both wait: one a day, the missed one ten minutes");
        deck.Session(all, T0.AddMinutes(11)).Select(i => i.Id).Should().Equal(all[1].Id);
        deck.Session(all, T0.AddDays(1).AddMinutes(1)).Should().HaveCount(2);

        new NoteDeck(_dir).Cards.Should().HaveCount(2, "the progress is saved");
    }

    [Fact]
    public async Task The_review_opens_each_game_in_turn_and_moves_the_cards()
    {
        SaveGame("a", 0);
        SaveGame("b", 1);
        var deck = new NoteDeck(_dir);
        var game = new GameViewModel(9, RuleSet.Japanese);
        var main = new MainWindowViewModel(game, replays: _store, notes: deck) { Now = () => T0 };

        await main.RefreshNotesDueAsync();
        main.NotesDue.Should().Be(2);
        main.ReviewNotesHeader.Should().Be("Repasar mis notas (2)");

        await main.ReviewNotesCommand.ExecuteAsync(null);
        main.Study.IsNoteReview.Should().BeTrue();
        main.Study.QuizTitle.Should().Be("Repasar mis notas");
        GameTree first = game.Tree;
        game.MoveNumber.Should().Be(2, "the position before the pinned move");
        main.Study.BoardClickCommand.Execute(main.Study.Question!.Answer!.Value);
        main.Study.QuizResult.Should().Be("¡Correcto!");
        game.StatusText.Should().Be("Volverá mañana.");
        main.NotesDue.Should().Be(1);

        main.Study.NextQuestionCommand.Execute(null);
        main.Study.IsQuizActive.Should().BeTrue("the next game opens without ending the review");
        game.Tree.Should().NotBeSameAs(first);
        main.Study.RevealAnswerCommand.Execute(null);
        main.Study.NextQuestionCommand.Execute(null);
        main.Study.QuizResult.Should().Be("Repaso terminado: 1 de 2 acertadas. Volverán cuando toque.");

        deck.Cards.Values.Select(c => c.Box).Order().Should().Equal(1, 2);
        main.Study.EndQuizCommand.Execute(null);
        main.Study.IsNoteReview.Should().BeFalse();

        main.Now = () => T0.AddMinutes(2);
        await main.ReviewNotesCommand.ExecuteAsync(null);
        main.Study.IsQuizActive.Should().BeFalse();
        game.StatusText.Should().Be("Nada que repasar hoy: tus notas volverán cuando toque.");
    }

    [Fact]
    public void Note_progress_travels_in_backups_and_the_latest_review_wins()
    {
        string other = _dir + "-other";
        try
        {
            new NoteDeck(_dir).Grade("g#3#p", right: true, T0);
            var theirs = new NoteDeck(other);
            theirs.Grade("g#3#p", right: true, T0.AddDays(1));
            theirs.Grade("h#5#q", right: false, T0.AddDays(1));

            BackupSummary summary = BackupPackage.Merge(other, _dir, applyPreferences: false);

            summary.NoteCards.Should().Be(2);
            IReadOnlyDictionary<string, Hoshi.Sgf.Joseki.JosekiCard> cards = new NoteDeck(_dir).Cards;
            cards["g#3#p"].LastReview.Should().Be(T0.AddDays(1));
            cards.Should().ContainKey("h#5#q");
        }
        finally
        {
            if (Directory.Exists(other))
            {
                Directory.Delete(other, recursive: true);
            }
        }
    }
}
