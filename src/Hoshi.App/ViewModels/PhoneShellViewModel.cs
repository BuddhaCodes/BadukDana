using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
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
    private readonly ISettingsService? _settings;

    /// <summary>The sheet's height when nothing was dragged yet.</summary>
    public const double DefaultSheetHeight = 330;

    /// <summary>The smallest sheet; dragged lower than <see cref="CloseSheetHeight"/>, the sheet closes.</summary>
    public const double MinSheetHeight = 140;

    public const double CloseSheetHeight = 100;

    /// <summary>Room the board keeps above the sheet, at least.</summary>
    public const double MinBoardRoom = 220;

    /// <summary>The sheet's height (dragged by its handle, kept between sessions).</summary>
    [ObservableProperty]
    private double _sheetHeight = DefaultSheetHeight;

    public PhoneShellViewModel(MainWindowViewModel main, PhoneNavigation navigation, ISettingsService? settings = null)
    {
        Main = main ?? throw new ArgumentNullException(nameof(main));
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings;
        if (settings?.Current.PhoneSheetHeight is { } height)
        {
            _sheetHeight = Math.Max(MinSheetHeight, height);
        }

        // A short buzz with every stone (yours, the opponent's, or stepping forward), when wanted.
        main.Game.StonePlaced += (_, _) =>
        {
            if (_settings?.Current.Haptics ?? true)
            {
                PlatformInfo.Vibrate?.Invoke();
            }
        };

        // The screen stays on while a game is going (an online game, or moves on the board and not over yet).
        main.Game.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameViewModel.MoveNumber) or nameof(GameViewModel.IsGameOver))
            {
                UpdateKeepAwake();
            }
        };
        UpdateKeepAwake();

        // The study and the analysis also open by themselves (a shared study, the A key on a keyboard): follow them.
        main.Study.PropertyChanged += OnStudyChanged;
        main.Analysis.PropertyChanged += OnAnalysisChanged;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsOnline) && !main.IsOnline && Sheet == PhoneSheet.Chat)
            {
                Sheet = PhoneSheet.None;
            }

            if (e.PropertyName is nameof(MainWindowViewModel.IsOnline))
            {
                UpdateKeepAwake();
            }

            if (e.PropertyName is nameof(MainWindowViewModel.IsZen) && main.IsZen)
            {
                Sheet = PhoneSheet.None; // zen: the board alone
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

    /// <summary>
    /// The sheet's handle is being dragged: <paramref name="height"/> is where the finger would put it, within
    /// <paramref name="available"/> (the height of the whole layout). Below <see cref="MinSheetHeight"/> it stays there
    /// until let go, so the finger can tell it is about to close.
    /// </summary>
    public void DragSheet(double height, double available)
    {
        double max = Math.Max(MinSheetHeight, available - MinBoardRoom);
        SheetHeight = Math.Clamp(height, CloseSheetHeight * 0.6, max);
    }

    /// <summary>The handle was let go: a sheet dragged almost away closes (and keeps its old height), else it is kept.</summary>
    public void EndSheetDrag(double heightBefore)
    {
        if (SheetHeight < CloseSheetHeight)
        {
            SheetHeight = heightBefore;
            Sheet = PhoneSheet.None;
            return;
        }

        SheetHeight = Math.Max(MinSheetHeight, SheetHeight);
        if (_settings is not null && Math.Abs((_settings.Current.PhoneSheetHeight ?? DefaultSheetHeight) - SheetHeight) >= 1)
        {
            _settings.Save(_settings.Current with { PhoneSheetHeight = Math.Round(SheetHeight) });
        }
    }

    /// <summary>The screen is kept on (a game is going).</summary>
    public bool KeepAwake { get; private set; }

    private void UpdateKeepAwake()
    {
        bool awake = Main.IsOnline || (Main.Game.MoveNumber > 0 && !Main.Game.IsGameOver);
        if (awake != KeepAwake)
        {
            KeepAwake = awake;
            PlatformInfo.KeepScreenOn?.Invoke(awake);
        }
    }

    /// <summary>A swipe on the header: steps forward (positive) or back (negative) through the game.</summary>
    public void Step(int steps)
    {
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            System.Windows.Input.ICommand command = steps > 0 ? Main.Game.GoForwardCommand : Main.Game.GoBackCommand;
            if (!command.CanExecute(null))
            {
                return;
            }

            command.Execute(null);
        }
    }

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

        if (Main.IsZen)
        {
            Main.IsZen = false;
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
