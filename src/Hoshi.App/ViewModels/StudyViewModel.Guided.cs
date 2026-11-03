using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.Core.Localization;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Guided review ----------

    private IReadOnlyList<KeyMistake> _guided = [];
    private int _guidedNotes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuidedProgress), nameof(GuidedPrompt))]
    private int _guidedIndex;

    [ObservableProperty]
    private bool _isGuided;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuidedSaveCommand))]
    private string _guidedText = string.Empty;

    /// <summary>KataGo has found the game's biggest mistakes (it reads the game while analysis is on).</summary>
    public bool CanStartGuided => _analysis is { HasMistakes: true } && !IsLiveOnline;

    public string GuidedProgress => IsGuided ? Tr.F("Study.GuidedProgress", GuidedIndex + 1, _guided.Count) : string.Empty;

    /// <summary>"Move 34, Black K10: −5.2. KataGo prefers Q16. What went wrong?"</summary>
    public string GuidedPrompt => IsGuided && GuidedIndex < _guided.Count && _guided[GuidedIndex] is { } m
        ? Tr.F("Study.GuidedPrompt", m.Number, Tr.T(m.IsBlack ? "Common.Black" : "Common.White"), m.Move, m.LossText, m.Better)
        : string.Empty;

    /// <summary>
    /// "Guided review": after a game, one step per big mistake KataGo found (up to three): the board goes to the
    /// move, Hoshi says what it cost and what KataGo preferred, and asks the player to write, in their own words,
    /// what went wrong. Each answer becomes a "mistake" note (which then comes back in the review of notes);
    /// "Explain" adds KataGo's line. Never during your live OGS game (KataGo reads nothing there).
    /// </summary>
    [RelayCommand]
    private void StartGuided()
    {
        if (!CanStartGuided || _analysis is null)
        {
            _game.ShowStatus(Tr.T("Study.GuidedNothing"));
            return;
        }

        if (IsQuizActive)
        {
            EndQuiz();
        }

        _guided = [.. _analysis.KeyMistakes];
        _guidedNotes = 0;
        IsActive = true;
        IsGuided = true;
        ShowGuided(0);
    }

    private bool CanSaveGuided() => GuidedText.Trim().Length > 0;

    /// <summary>Keeps the player's words as a mistake note on the move, and goes on.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveGuided))]
    private void GuidedSave()
    {
        if (!IsGuided || GuidedIndex >= _guided.Count)
        {
            return;
        }

        _game.GoToNodeCommand.Execute(_guided[GuidedIndex].Node);
        AddPin(PinCategory.Mistake, GuidedText.Trim());
        _guidedNotes++;
        GuidedNext();
    }

    [RelayCommand]
    private void GuidedNext()
    {
        if (GuidedIndex + 1 < _guided.Count)
        {
            ShowGuided(GuidedIndex + 1);
            return;
        }

        int notes = _guidedNotes;
        EndGuided();
        _game.ShowStatus(Tr.F("Study.GuidedDone", notes));
    }

    [RelayCommand]
    private void EndGuided()
    {
        IsGuided = false;
        _guided = [];
        GuidedText = string.Empty;
        OnPropertyChanged(nameof(GuidedProgress));
        OnPropertyChanged(nameof(GuidedPrompt));
    }

    private void ShowGuided(int index)
    {
        GuidedText = string.Empty;
        GuidedIndex = index;
        _game.GoToNodeCommand.Execute(_guided[index].Node);
        OnPropertyChanged(nameof(GuidedProgress));
        OnPropertyChanged(nameof(GuidedPrompt));
    }
}
