using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services.Study;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Presentation ----------

    private IReadOnlyList<GameNode> _slides = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlideProgress), nameof(SlideTitle), nameof(SlideText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousSlideCommand), nameof(NextSlideCommand))]
    private int _slideIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlideProgress), nameof(SlideTitle), nameof(SlideText))]
    private bool _isPresenting;

    public string SlideProgress => IsPresenting ? Tr.F("Study.SlideProgress", SlideIndex + 1, _slides.Count) : string.Empty;

    /// <summary>"Move 34 · Mistake, Key moment".</summary>
    public string SlideTitle => Slide is { } node
        ? string.Join(" · ", new[] { Tr.F("Notebook.Move", GameCursor.Path(node).Count(n => n.HasMove)) }
            .Concat(StudyStore.Read(node).Pins.Select(p => StudyReport.CategoryName(p.Category)).Distinct()))
        : string.Empty;

    /// <summary>The notes of the moment (with their authors) and the last reply of each thread.</summary>
    public string SlideText => Slide is { } node
        ? string.Join("\n", StudyStore.Read(node).Pins.Select(p =>
            (p.Text.Length > 0 ? p.Text : StudyReport.CategoryName(p.Category)) + " — " + p.Author
            + (p.Replies.Count > 0 ? "\n   ↳ " + p.Replies[^1].Author + ": " + p.Replies[^1].Text : string.Empty)))
        : string.Empty;

    private GameNode? Slide => IsPresenting && SlideIndex < _slides.Count ? _slides[SlideIndex] : null;

    /// <summary>
    /// Presentation mode: the studied moments one after another (every move with notes or drawings, in game order),
    /// the board large with the drawings and the notes in a card; for a lesson, a club or a screen share. The main
    /// window hides its side panels meanwhile.
    /// </summary>
    [RelayCommand]
    private void StartPresentation()
    {
        _slides = [.. StudyStore.All(_game.Tree).Where(x => !x.Study.IsEmpty).Select(x => x.Node)];
        if (_slides.Count == 0)
        {
            _game.ShowStatus(Tr.T("Study.PresentNothing"));
            return;
        }

        if (IsQuizActive)
        {
            EndQuiz();
        }

        SlideIndex = 0;
        IsPresenting = true;
        _game.GoToNodeCommand.Execute(_slides[0]);
        PreviousSlideCommand.NotifyCanExecuteChanged();
        NextSlideCommand.NotifyCanExecuteChanged();
    }

    private bool HasPrevious => IsPresenting && SlideIndex > 0;

    private bool HasNext => IsPresenting && SlideIndex + 1 < _slides.Count;

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void PreviousSlide() => Show(SlideIndex - 1);

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void NextSlide() => Show(SlideIndex + 1);

    [RelayCommand]
    private void StopPresentation()
    {
        IsPresenting = false;
        _slides = [];
    }

    private void Show(int index)
    {
        if (index < 0 || index >= _slides.Count)
        {
            return;
        }

        SlideIndex = index;
        _game.GoToNodeCommand.Execute(_slides[index]);
    }
}
