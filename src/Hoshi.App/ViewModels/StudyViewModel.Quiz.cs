using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Quiz: "What would you play?" ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardDrawings), nameof(GhostStone), nameof(DragPreview))]
    private bool _isQuizActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuizProgress), nameof(QuizPrompt), nameof(QuizNote), nameof(HasQuizNote), nameof(IsQuizScored), nameof(CanReveal))]
    private QuizQuestion? _question;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostStone), nameof(BoardDrawings), nameof(QuizNote), nameof(HasQuizNote), nameof(CanSelfGrade), nameof(CanReveal), nameof(CanGoNext))]
    private bool _isQuizAnswered;

    [ObservableProperty]
    private string _quizResult = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardDrawings))]
    private IReadOnlyList<StudyDrawing> _quizOverlay = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuizScore))]
    private int _quizRight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuizScore))]
    private int _quizDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelfGrade), nameof(CanGoNext))]
    private bool _isGraded;

    [ObservableProperty]
    private bool _isQuizFinished;

    private int _quizIndex;

    // The note review: questions from many games of the library, each opened when its turn comes.
    private IReadOnlyList<NoteReviewItem>? _review;
    private Action<NoteReviewItem>? _openReview;
    private Action<NoteReviewItem, bool>? _gradedReview;
    private bool _openingReviewGame;

    /// <summary>"Quiz", or "Review my notes" during the note review (with the game the question comes from).</summary>
    public string QuizTitle => _review is null ? Tr.T("Study.Quiz") : Tr.T("Notes.Menu");

    /// <summary>True while the quiz is the spaced review of notes from the library (not this game's quiz).</summary>
    public bool IsNoteReview => _review is not null && IsQuizActive;

    /// <summary>
    /// Starts the review of notes from the library: <paramref name="open"/> puts a question's game on the board (the
    /// very tree the question was built from), <paramref name="graded"/> moves its card. Not during a live OGS game.
    /// </summary>
    public bool StartNoteReview(IReadOnlyList<NoteReviewItem> items, Action<NoteReviewItem> open, Action<NoteReviewItem, bool> graded)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(graded);
        if (items.Count == 0 || IsLiveOnline)
        {
            return false;
        }

        _review = items;
        _openReview = open;
        _gradedReview = graded;
        _quiz = [.. items.Select(i => i.Question)];
        IsActive = true;
        QuizRight = 0;
        QuizDone = 0;
        IsQuizFinished = false;
        IsQuizActive = true;
        OnPropertyChanged(nameof(IsNoteReview));
        OnPropertyChanged(nameof(QuizTitle));
        Ask(0);
        return true;
    }

    private void ReportGrade(QuizQuestion q, bool right)
    {
        if (_review is { } items && _gradedReview is { } graded && items.FirstOrDefault(i => ReferenceEquals(i.Question, q)) is { } item)
        {
            graded(item, right);
        }
    }

    public string QuizProgress => Question is null ? string.Empty : Tr.F("Study.QuizProgress", _quizIndex + 1, _quiz.Count);

    public string QuizPrompt => Question is { } q
        ? Tr.F("Study.QuizPrompt", Tr.T(q.ToPlay == Stone.Black ? "Common.Black" : "Common.White"))
        : string.Empty;

    public string QuizScore => Tr.F("Study.QuizScore", QuizRight, QuizDone);

    public bool IsQuizScored => Question?.IsScored == true;

    public bool CanSelfGrade => IsQuizAnswered && !IsQuizScored && !IsGraded;

    /// <summary>"Show" is offered while a question waits for an answer.</summary>
    public bool CanReveal => Question is not null && !IsQuizAnswered;

    /// <summary>"Next" once the answer is shown (and, for unscored questions, graded).</summary>
    public bool CanGoNext => Question is not null && IsQuizAnswered && IsGraded;

    /// <summary>After answering: the pin that made the question (its category, author and note).</summary>
    public string QuizNote => IsQuizAnswered && Question is { } q
        ? $"{StudyReport.CategoryName(q.Pin.Category)} · {q.Pin.Author}{(q.Pin.Text.Length > 0 ? ": " + q.Pin.Text : string.Empty)}"
        : string.Empty;

    public bool HasQuizNote => QuizNote.Length > 0;

    [RelayCommand(CanExecute = nameof(CanStartQuiz))]
    private void StartQuiz()
    {
        _quiz = StudyQuiz.Build(_game.Tree);
        if (_quiz.Count == 0)
        {
            _game.ShowStatus(Tr.T("Study.QuizEmpty"));
            return;
        }

        IsActive = true;
        QuizRight = 0;
        QuizDone = 0;
        IsQuizFinished = false;
        IsQuizActive = true;
        Ask(0);
    }

    // Moving through the game is fine online, but the quiz takes the board clicks: not during a live game.
    private bool CanStartQuiz() => !IsLiveOnline;

    [RelayCommand]
    private void NextQuestion()
    {
        if (_quizIndex + 1 >= _quiz.Count)
        {
            IsQuizFinished = true;
            Question = null;
            QuizOverlay = [];
            QuizResult = Tr.F(_review is null ? "Study.QuizDone" : "Study.ReviewDone", QuizRight, QuizDone);
            return;
        }

        Ask(_quizIndex + 1);
    }

    /// <summary>Shows the answer without guessing (counts as a miss).</summary>
    [RelayCommand]
    private void RevealAnswer()
    {
        if (Question is { } q && !IsQuizAnswered)
        {
            Grade(q, guess: null);
        }
    }

    [RelayCommand]
    private void GotIt() => GradeSelf(right: true);

    [RelayCommand]
    private void Missed() => GradeSelf(right: false);

    private void GradeSelf(bool right)
    {
        if (!CanSelfGrade)
        {
            return;
        }

        IsGraded = true;
        if (right)
        {
            QuizRight++;
        }

        ReportGrade(Question!, right);
        QuizResult = Tr.T(right ? "Study.QuizSelfRight" : "Study.QuizSelfWrong");
    }

    [RelayCommand]
    private void EndQuiz()
    {
        IsQuizActive = false;
        IsQuizFinished = false;
        _review = null;
        _openReview = null;
        _gradedReview = null;
        OnPropertyChanged(nameof(IsNoteReview));
        OnPropertyChanged(nameof(QuizTitle));
        Question = null;
        QuizOverlay = [];
        QuizResult = string.Empty;
        BoardClickCommand.NotifyCanExecuteChanged();
    }

    private void Ask(int index)
    {
        _quizIndex = index;
        QuizQuestion q = _quiz[index];
        if (_review is { } items && _openReview is { } open && !ReferenceEquals(_game.Tree, items[index].Tree))
        {
            _openingReviewGame = true;
            try
            {
                open(items[index]);
            }
            finally
            {
                _openingReviewGame = false;
            }
        }

        IsQuizAnswered = false;
        IsGraded = false;
        QuizOverlay = [];
        QuizResult = string.Empty;
        Question = q;
        _game.GoToNodeCommand.Execute(q.Position);
        OnPropertyChanged(nameof(QuizProgress));
        BoardClickCommand.NotifyCanExecuteChanged();
    }

    private void AnswerQuiz(Point point)
    {
        if (Question is not { } q || IsQuizAnswered || !_game.Board.IsOnBoard(point) || _game.Board[point] != Stone.Empty)
        {
            return;
        }

        Grade(q, point);
    }

    private void Grade(QuizQuestion q, Point? guess)
    {
        var overlay = new List<StudyDrawing>();
        int h = _game.Board.Height;
        QuizDone++;
        if (q.IsScored)
        {
            bool right = guess is { } g && StudyQuiz.IsRight(q, g);
            if (right)
            {
                QuizRight++;
            }

            overlay.Add(new StudyDrawing("quiz-answer", DrawingKind.Mark, string.Empty, StudyColor.Green, [q.Answer!.Value]));
            if (guess is { } wrong && !right)
            {
                overlay.Add(new StudyDrawing("quiz-guess", DrawingKind.Mark, string.Empty, StudyColor.Red, [wrong]));
            }

            ReportGrade(q, right);
            QuizResult = right
                ? Tr.T("Study.QuizRight")
                : Tr.F("Study.QuizWrong", q.Answer.Value.ToHuman(h));
            IsGraded = true;
        }
        else
        {
            if (guess is { } g)
            {
                overlay.Add(new StudyDrawing("quiz-guess", DrawingKind.Mark, string.Empty, StudyColor.Blue, [g]));
            }

            QuizResult = guess is null ? Tr.T("Study.QuizRevealed") : Tr.T("Study.QuizCompare");
            IsGraded = guess is null;
            if (guess is null)
            {
                ReportGrade(q, right: false); // shown without trying: comes back soon
            }
        }

        // What was played in the game: for a mistake, the move to avoid.
        if (q.Played is { } played && played != q.Answer)
        {
            overlay.Add(new StudyDrawing("quiz-played", DrawingKind.Label, string.Empty, q.Pin.Category == PinCategory.Mistake ? StudyColor.Red : StudyColor.Gold, [played], "!"));
        }

        // The pinned move's own drawings (the "what if" lines) help explain the answer.
        overlay.InsertRange(0, StudyStore.Read(q.Position).Drawings.Concat(StudyStore.Read(q.Pinned).Drawings));
        QuizOverlay = overlay;
        IsQuizAnswered = true;
    }
}
