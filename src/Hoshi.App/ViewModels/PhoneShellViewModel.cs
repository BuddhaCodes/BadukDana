using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services.Phone;

namespace Hoshi.App.ViewModels;

/// <summary>The sheet that slides up under the board on a phone (one at a time).</summary>
public enum PhoneSheet
{
    None,

    /// <summary>The variation tree and the comment.</summary>
    Moves,
    Analysis,
    Study,

    /// <summary>The game chat, during online games.</summary>
    Chat,
    Menu,
}

/// <summary>
/// The phone layout: the board fills the screen, a bar of buttons below, and one sheet at a time for the
/// moves, the analysis, the study, the chat or the menu. Windows open as pages (<see cref="PhoneNavigation"/>).
/// </summary>
public sealed partial class PhoneShellViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoves), nameof(IsAnalysis), nameof(IsStudy), nameof(IsChat), nameof(IsMenu), nameof(HasSheet))]
    private PhoneSheet _sheet;

    private bool _syncing;

    public PhoneShellViewModel(MainWindowViewModel main, PhoneNavigation navigation)
    {
        Main = main ?? throw new ArgumentNullException(nameof(main));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

        // The study and the analysis also open by themselves (a shared study, the A key on a keyboard): follow them.
        main.Study.PropertyChanged += OnStudyChanged;
        main.Analysis.PropertyChanged += OnAnalysisChanged;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsOnline) && !main.IsOnline && Sheet == PhoneSheet.Chat)
            {
                Sheet = PhoneSheet.None;
            }
        };
        navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PhoneNavigation.HasPage) && navigation.HasPage && Sheet == PhoneSheet.Menu)
            {
                Sheet = PhoneSheet.None;
            }
        };
    }

    public MainWindowViewModel Main { get; }

    public PhoneNavigation Navigation { get; }

    public bool IsMoves => Sheet == PhoneSheet.Moves;

    public bool IsAnalysis => Sheet == PhoneSheet.Analysis;

    public bool IsStudy => Sheet == PhoneSheet.Study;

    public bool IsChat => Sheet == PhoneSheet.Chat;

    public bool IsMenu => Sheet == PhoneSheet.Menu;

    public bool HasSheet => Sheet != PhoneSheet.None;

    /// <summary>The bar buttons: a second tap closes the sheet.</summary>
    [RelayCommand]
    private void ToggleSheet(PhoneSheet sheet) => Sheet = Sheet == sheet ? PhoneSheet.None : sheet;

    /// <summary>A menu entry was chosen: the menu goes away.</summary>
    public void CloseMenu()
    {
        if (Sheet == PhoneSheet.Menu)
        {
            Sheet = PhoneSheet.None;
        }
    }

    /// <summary>Android's back button: a message or page first, then the sheet, then a running quiz. False = leave the app.</summary>
    public bool Back()
    {
        if (Navigation.Back())
        {
            return true;
        }

        if (Sheet != PhoneSheet.None)
        {
            Sheet = PhoneSheet.None;
            return true;
        }

        if (Main.Study.IsQuizActive)
        {
            Main.Study.EndQuizCommand.Execute(null);
            return true;
        }

        return false;
    }

    partial void OnSheetChanged(PhoneSheet oldValue, PhoneSheet newValue)
    {
        _syncing = true;
        try
        {
            // The study's drawings and tools belong to its sheet; the analysis panel to its own.
            if (newValue == PhoneSheet.Study || oldValue == PhoneSheet.Study)
            {
                Main.Study.IsActive = newValue == PhoneSheet.Study;
            }

            if (newValue == PhoneSheet.Analysis || oldValue == PhoneSheet.Analysis)
            {
                Main.Analysis.IsAnalysisOn = newValue == PhoneSheet.Analysis;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnStudyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_syncing && e.PropertyName == nameof(StudyViewModel.IsActive))
        {
            if (Main.Study.IsActive)
            {
                Sheet = PhoneSheet.Study;
            }
            else if (Sheet == PhoneSheet.Study)
            {
                Sheet = PhoneSheet.None;
            }
        }
    }

    private void OnAnalysisChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_syncing && e.PropertyName == nameof(AnalysisViewModel.IsAnalysisOn))
        {
            if (Main.Analysis.IsAnalysisOn)
            {
                Sheet = PhoneSheet.Analysis;
            }
            else if (Sheet == PhoneSheet.Analysis)
            {
                Sheet = PhoneSheet.None;
            }
        }
    }
}
