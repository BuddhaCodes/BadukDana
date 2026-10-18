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

/// <summary>A player's card in an online game on the phone (the opponent's above the board, yours below).</summary>
public sealed record PlayerCard(string Name, string Clock, bool IsClockLow, int Captures, bool IsBlack, bool IsToMove, bool IsYou)
{
    public string CapturesText => Captures > 0 ? "+" + Captures.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
}

/// <summary>What a tile of the phone's home screen does.</summary>
public enum PhoneHomeAction
{
    New19,
    New13,
    New9,
    Online,
    KataGo,
    Problems,

    /// <summary>Open an SGF file.</summary>
    Open,
    Study,
    Replays,
}

/// <summary>
/// The phone layout: the board fills the screen, a bar of buttons below, and one sheet at a time for the
/// moves, the analysis, the study, the chat or the menu. Windows open as pages (<see cref="PhoneNavigation"/>).
/// </summary>
public sealed partial class PhoneShellViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoves), nameof(IsAnalysis), nameof(IsStudy), nameof(IsChat), nameof(IsMenu), nameof(HasSheet), nameof(IsReview))]
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
    [NotifyPropertyChangedFor(nameof(SheetHeightValue))]
    private double _sheetHeight = DefaultSheetHeight;

    /// <summary>Wider than tall (a tablet, or a phone turned): the sheet goes to the right of the board.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetDock), nameof(SheetHeightValue), nameof(SheetWidthValue), nameof(SheetBorder))]
    private bool _isLandscape;

    private double _width;

    /// <summary>Landscape needs at least this width (narrower screens keep the sheet under the board).</summary>
    public const double LandscapeMinWidth = 600;

    public Avalonia.Controls.Dock SheetDock => IsLandscape ? Avalonia.Controls.Dock.Right : Avalonia.Controls.Dock.Bottom;

    public double SheetHeightValue => IsLandscape ? double.NaN : SheetHeight;

    /// <summary>In landscape: 40 % of the width, between 300 and 520.</summary>
    public double SheetWidthValue => IsLandscape ? Math.Clamp(_width * 0.4, 300, 520) : double.NaN;

    public Avalonia.Thickness SheetBorder => IsLandscape ? new Avalonia.Thickness(1, 0, 0, 0) : new Avalonia.Thickness(0, 1, 0, 0);

    /// <summary>The view's size changed (rotation, a tablet's split screen).</summary>
    public void UpdateSize(double width, double height)
    {
        _width = width;
        IsLandscape = width >= LandscapeMinWidth && width > height * 1.1;
        OnPropertyChanged(nameof(SheetWidthValue));
    }

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
            if (e.PropertyName is nameof(GameViewModel.Board))
            {
                CardsChanged();
            }

            if (e.PropertyName is nameof(GameViewModel.MoveNumber) or nameof(GameViewModel.IsGameOver))
            {
                UpdateKeepAwake();
                OnPropertyChanged(nameof(HasGameToContinue));
                OnPropertyChanged(nameof(ContinueDetail));
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

            if (e.PropertyName is nameof(MainWindowViewModel.Online) or nameof(MainWindowViewModel.IsOnline))
            {
                WatchOnline();
            }

            if (e.PropertyName is nameof(MainWindowViewModel.IsZen))
            {
                if (main.IsZen)
                {
                    Sheet = PhoneSheet.None; // zen: the board alone
                }

                PlatformInfo.Immersive?.Invoke(main.IsZen); // and no system bars
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

    /// <summary>
    /// The home screen (redesign 2026-10): continue the game, start a new one, or pick a mode. Shown when the app
    /// starts and by the back button once nothing else is left to close; the board is under it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameToContinue), nameof(ContinueTitle), nameof(ContinueDetail))]
    private bool _isHome;

    /// <summary>The opponent's card, above the board in an online game (white when you only watch).</summary>
    public PlayerCard? TopCard => Card(top: true);

    /// <summary>Your card, under the board (black when you only watch).</summary>
    public PlayerCard? BottomCard => Card(top: false);

    private PlayerCard? Card(bool top)
    {
        if (Main.Online is not { } online)
        {
            return null;
        }

        Core.Stone mine = online.IsPlayer ? online.MyColor : Core.Stone.Black;
        bool black = top ? mine != Core.Stone.Black : mine == Core.Stone.Black;
        return new PlayerCard(
            black ? online.BlackName : online.WhiteName,
            black ? online.BlackClock : online.WhiteClock,
            black ? online.IsBlackClockLow : online.IsWhiteClockLow,
            black ? Main.Game.Board.BlackCaptures : Main.Game.Board.WhiteCaptures,
            black,
            online.IsPlayPhase && online.ToMove == (black ? Core.Stone.Black : Core.Stone.White),
            online.IsPlayer && !top);
    }

    private OnlineGameViewModel? _watched;

    private void WatchOnline()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnOnlineChanged;
        }

        _watched = Main.Online;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnOnlineChanged;
        }

        CardsChanged();
    }

    private void OnOnlineChanged(object? sender, PropertyChangedEventArgs e) => CardsChanged();

    private void CardsChanged()
    {
        OnPropertyChanged(nameof(TopCard));
        OnPropertyChanged(nameof(BottomCard));
    }

    /// <summary>There is something on the board to go back to (moves played, or an online game).</summary>
    public bool HasGameToContinue => Main.IsOnline || Main.Game.MoveNumber > 0;

    public string ContinueTitle => Main.IsOnline
        ? Main.Game.BlackName + " · " + Main.Game.WhiteName
        : Main.Game.HeaderTitle;

    public string ContinueDetail => Main.Game.MoveNumberText + (Main.Game.IsGameOver ? string.Empty : " · " + Main.Game.StatusText);

    /// <summary>Opens the home screen (the sheets close).</summary>
    [RelayCommand]
    private void ShowHome()
    {
        Sheet = PhoneSheet.None;
        IsHome = true;
    }

    /// <summary>Back to the board as it is.</summary>
    [RelayCommand]
    private void ContinueGame() => IsHome = false;

    /// <summary>A home tile: leaves the home screen and does what the tile says.</summary>
    [RelayCommand]
    private void Home(PhoneHomeAction action)
    {
        IsHome = false;
        switch (action)
        {
            case PhoneHomeAction.New19 or PhoneHomeAction.New13 or PhoneHomeAction.New9:
                int size = action switch { PhoneHomeAction.New13 => 13, PhoneHomeAction.New9 => 9, _ => 19 };
                if (Main.Game.NewGameCommand.CanExecute(size))
                {
                    Main.Game.NewGameCommand.Execute(size);
                }

                break;
            case PhoneHomeAction.Online:
                Execute(Main.OpenLobbyCommand);
                break;
            case PhoneHomeAction.KataGo:
                Execute(Main.PlayEngineCommand);
                break;
            case PhoneHomeAction.Problems:
                Execute(Main.OpenTsumegoCommand);
                break;
            case PhoneHomeAction.Open:
                Execute(Main.Game.OpenCommand);
                break;
            case PhoneHomeAction.Study:
                Sheet = PhoneSheet.Study;
                break;
            case PhoneHomeAction.Replays:
                Execute(Main.OpenReplaysCommand);
                break;
        }

        static void Execute(System.Windows.Input.ICommand command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }

    public bool IsMoves => Sheet == PhoneSheet.Moves;

    public bool IsAnalysis => Sheet == PhoneSheet.Analysis;

    public bool IsStudy => Sheet == PhoneSheet.Study;

    public bool IsChat => Sheet == PhoneSheet.Chat;

    public bool IsMenu => Sheet == PhoneSheet.Menu;

    public bool HasSheet => Sheet != PhoneSheet.None;

    /// <summary>The moves, the analysis or the study: one sheet with a switch on top (one «Review» button).</summary>
    public bool IsReview => Sheet is PhoneSheet.Moves or PhoneSheet.Analysis or PhoneSheet.Study;

    /// <summary>The part of «Review» last shown; the button opens it again.</summary>
    public PhoneSheet LastReview { get; private set; } = PhoneSheet.Moves;

    /// <summary>The bar buttons: a second tap closes the sheet.</summary>
    [RelayCommand]
    private void ToggleSheet(PhoneSheet sheet) => Sheet = Sheet == sheet ? PhoneSheet.None : sheet;

    /// <summary>The switch inside a sheet: shows that part (never closes).</summary>
    [RelayCommand]
    private void ShowSheet(PhoneSheet sheet) => Sheet = sheet;

    /// <summary>«Review»: opens the part last used (the moves at first), or closes the sheet.</summary>
    [RelayCommand]
    private void ToggleReview()
    {
        if (IsReview)
        {
            Sheet = PhoneSheet.None;
            return;
        }

        Sheet = LastReview == PhoneSheet.Analysis && Main.Analysis.IsBlocked ? PhoneSheet.Moves : LastReview;
    }

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

        if (!IsHome)
        {
            IsHome = true; // the board → home; back again leaves the app
            return true;
        }

        return false;
    }

    partial void OnSheetChanged(PhoneSheet oldValue, PhoneSheet newValue)
    {
        if (newValue is PhoneSheet.Moves or PhoneSheet.Analysis or PhoneSheet.Study)
        {
            LastReview = newValue;
        }

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
