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

/// <summary>The parts of the study panel; phones show one at a time (desktops show them all).</summary>
public enum StudySection
{
    /// <summary>This move's notes and replies, and a new note.</summary>
    Move,

    /// <summary>Drawing tools and colours.</summary>
    Draw,

    /// <summary>The notes along the game, saving, the report and the quiz.</summary>
    Game,
}

/// <summary>What a click (or a drag) on the board does while the study panel is open.</summary>
public enum StudyTool
{
    /// <summary>Clicks play as usual (or go to OGS online); pins are added from the panel.</summary>
    Note,
    Arrow,
    Area,
    Mark,
    Label,

    /// <summary>A "what if" line of numbered stones, drawn without touching the game.</summary>
    Sequence,
    Erase,
}

/// <summary>A pin on the timeline: which move, its category's colour and a tooltip.</summary>
public sealed record TimelineMark(GameNode Node, int Move, PinCategory? Category, string Tip)
{
    public string Colour => Category is { } c ? StudyReport.Colour(c) : "#8a8f9e";
}

/// <summary>A category to pick for a new pin or to filter by.</summary>
public sealed record StudyCategoryOption(PinCategory? Category)
{
    public string Name => Category is { } c ? StudyReport.CategoryName(c) : Tr.T("Study.AllCategories");

    public IBrush Brush => new ImmutableSolidColorBrush(Color.Parse(Category is { } c ? StudyReport.Colour(c) : "#8a8f9e"));

    public override string ToString() => Name;
}

/// <summary>A reply under a pin.</summary>
public sealed record StudyReplyRow(string Author, string When, string Text);

/// <summary>A pin on the current move, with its replies and a box to answer.</summary>
public sealed partial class StudyPinRow : ObservableObject
{
    private readonly StudyViewModel _owner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplyCommand))]
    private string _replyText = string.Empty;

    public StudyPinRow(StudyViewModel owner, StudyPin pin)
    {
        _owner = owner;
        Pin = pin;
    }

    public StudyPin Pin { get; }

    public string CategoryName => StudyReport.CategoryName(Pin.Category);

    public IBrush Brush => new ImmutableSolidColorBrush(Color.Parse(StudyReport.Colour(Pin.Category)));

    public string Author => Pin.Author;

    public string When => StudyViewModel.FormatTime(Pin.Time);

    public string Text => Pin.Text;

    public bool HasText => Pin.Text.Length > 0;

    public IReadOnlyList<StudyReplyRow> Replies => [.. Pin.Replies.Select(r => new StudyReplyRow(r.Author, StudyViewModel.FormatTime(r.Time), r.Text))];

    public bool HasReplies => Pin.Replies.Count > 0;

    [RelayCommand(CanExecute = nameof(CanReply))]
    private void Reply()
    {
        _owner.AddReply(this, ReplyText.Trim());
        ReplyText = string.Empty;
    }

    private bool CanReply() => ReplyText.Trim().Length > 0;

    [RelayCommand]
    private Task Delete() => _owner.DeletePinAsync(this);
}

/// <summary>
/// The study module: pins (notes with a category, author and replies), drawings on the board (arrows, areas,
/// marks, letters and "what if" sequences), a timeline of the pins, a "What would you play?" quiz built from them
/// and a game report. Everything lives in the game's SGF (property <c>HS</c>), so a study is shared as a file and
/// merged into the same game elsewhere. During a live OGS game it is notes only: no engine, and board clicks with
/// a drawing tool never reach OGS.
/// </summary>
public sealed partial class StudyViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly AnalysisViewModel? _analysis;
    private readonly ISettingsService? _settings;
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly IBrowserLauncher? _browser;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private GameTree? _tree;
    private GameNode? _node;
    private string? _sequenceId;
    private IReadOnlyList<QuizQuestion> _quiz = [];

    // Tap and confirm (phones): drawings made on the board wait here, for this move, until ✓ (or ✗).
    private IReadOnlyList<StudyDrawing>? _pendingDrawings;
    private GameNode? _pendingNode;

    // Arrows and areas by two taps: where the first tap was.
    private Point? _shapeStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardDrawings), nameof(GhostStone), nameof(DragPreview))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostStone), nameof(DragPreview), nameof(IsSequenceTool), nameof(ToolHint))]
    private StudyTool _tool = StudyTool.Note;

    [ObservableProperty]
    private StudyColor _color = StudyColor.Gold;

    [ObservableProperty]
    private StudyCategoryOption _newCategory = new(PinCategory.KeyMoment);

    [ObservableProperty]
    private string _newPinText = string.Empty;

    [ObservableProperty]
    private StudyCategoryOption _filter = new(null);

    [ObservableProperty]
    private string _author;

    [ObservableProperty]
    private IReadOnlyList<StudyPinRow> _pins = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardDrawings))]
    private IReadOnlyList<StudyDrawing> _drawings = [];

    [ObservableProperty]
    private IReadOnlyList<TimelineMark> _timeline = [];

    [ObservableProperty]
    private int _timelineTotal;

    [ObservableProperty]
    private int _timelineCurrent;

    [ObservableProperty]
    private bool _isBusy;

    public StudyViewModel(
        GameViewModel game,
        AnalysisViewModel? analysis = null,
        ISettingsService? settings = null,
        IFileDialogService? files = null,
        IDialogService? dialogs = null,
        IBrowserLauncher? browser = null,
        TimeProvider? time = null,
        ILogger<StudyViewModel>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _analysis = analysis;
        _settings = settings;
        _files = files;
        _dialogs = dialogs;
        _browser = browser;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _author = settings?.Current.StudyAuthor is { Length: > 0 } saved ? saved : Tr.T("Study.Me");
        Categories = [.. Enum.GetValues<PinCategory>().Select(c => new StudyCategoryOption(c))];
        Filters = [new StudyCategoryOption(null), .. Categories];
        _newCategory = Categories.First(c => c.Category == PinCategory.KeyMoment);
        _filter = Filters[0];
        _game.PropertyChanged += OnGamePropertyChanged;
        _game.PlayCommand.CanExecuteChanged += (_, _) => BoardClickCommand.NotifyCanExecuteChanged();
        if (_analysis is not null)
        {
            _analysis.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AnalysisViewModel.IsBlocked))
                {
                    OnPropertyChanged(nameof(IsLiveOnline));
                    StartQuizCommand.NotifyCanExecuteChanged();
                }
            };
        }

        Sync();
    }

    public IReadOnlyList<StudyCategoryOption> Categories { get; }

    public IReadOnlyList<StudyCategoryOption> Filters { get; }

    public IReadOnlyList<StudyColor> Colors { get; } = Enum.GetValues<StudyColor>();

    /// <summary>The player's own OGS game is in progress: notes and drawings only, no engine, no quiz.</summary>
    public bool IsLiveOnline => _game.Online is { IsPlayer: true, IsFinished: false };

    public bool HasPins => Pins.Count > 0;

    public bool IsSequenceTool => Tool == StudyTool.Sequence;

    /// <summary>One line under the tools on what the selected tool does.</summary>
    public string ToolHint => Tr.T("Study.Hint." + Tool);

    /// <summary>The move the pins below belong to ("Move 34 · Black Q16").</summary>
    public string MoveTitle => DescribeNode(_game.CurrentNode);

    public string TimelineText => Timeline.Count == 0
        ? Tr.T("Study.TimelineEmpty")
        : Tr.F("Study.TimelineCount", Timeline.Select(t => t.Node).Distinct().Count());

    /// <summary>What the board draws: this move's drawings while studying, plus the quiz's answer marks.</summary>
    public IReadOnlyList<StudyDrawing> BoardDrawings => IsQuizActive
        ? [.. QuizOverlay]
        : IsActive ? WithShapeStart(Drawings) : [];

    /// <summary>Id of the marker that shows where a two-tap arrow or area starts.</summary>
    public const string ShapeStartId = "shape-start";

    private IReadOnlyList<StudyDrawing> WithShapeStart(IReadOnlyList<StudyDrawing> drawings)
    {
        if (_shapeStart is not { } start)
        {
            return drawings;
        }

        // On a desktop the shape follows the mouse; on a phone the starting point is marked until the second tap.
        if (_game.HoverPoint is { } hover && hover != start && _game.Board.IsOnBoard(hover))
        {
            DrawingKind kind = Tool == StudyTool.Area ? DrawingKind.Area : DrawingKind.Arrow;
            return [.. drawings, new StudyDrawing(ShapeStartId, kind, AuthorName, Color, [start, hover])];
        }

        return [.. drawings, new StudyDrawing(ShapeStartId, DrawingKind.Mark, AuthorName, Color, [start])];
    }

    /// <summary>A two-tap arrow or area has its first point.</summary>
    public Point? ShapeStart => _shapeStart;

    /// <summary>The ghost stone under the mouse: the next sequence stone, the quiz's colour, nothing for the other tools.</summary>
    public Stone GhostStone
    {
        get
        {
            if (IsQuizActive)
            {
                return !IsQuizAnswered && _game.HoverPoint is { } q && _game.Board.IsOnBoard(q) && _game.Board[q] == Stone.Empty ? Question!.ToPlay : Stone.Empty;
            }

            if (!IsActive || Tool == StudyTool.Note)
            {
                return _game.GhostStone;
            }

            if (Tool == StudyTool.Sequence && _game.HoverPoint is { } p && _game.Board.IsOnBoard(p) && _game.Board[p] == Stone.Empty)
            {
                return NextSequenceColor();
            }

            return Stone.Empty;
        }
    }

    public DragPreview DragPreview => !IsActive || IsQuizActive ? DragPreview.None : Tool switch
    {
        StudyTool.Arrow => DragPreview.Arrow,
        StudyTool.Area => DragPreview.Area,
        _ => DragPreview.None,
    };

    // ---------- Panel ----------

    /// <summary>S or the "Study" button: opens or closes the panel (closing also ends a quiz).</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsActive && IsQuizActive)
        {
            EndQuiz();
        }

        IsActive = !IsActive;
    }

    [RelayCommand]
    private void SelectTool(StudyTool tool)
    {
        Tool = tool;
        if (tool == StudyTool.Sequence)
        {
            _sequenceId = null;
        }

        if (_shapeStart is not null)
        {
            _shapeStart = null;
            OnPropertyChanged(nameof(BoardDrawings));
            OnPropertyChanged(nameof(PendingDrawingIds));
        }
    }

    [RelayCommand]
    private void SelectColor(StudyColor color) => Color = color;

    [RelayCommand]
    private void SelectCategory(StudyCategoryOption? option)
    {
        if (option?.Category is not null)
        {
            NewCategory = option;
        }
    }

    /// <summary>The next sequence clicks start a new line (the current one stays).</summary>
    [RelayCommand]
    private void NewSequence() => _sequenceId = null;

    partial void OnAuthorChanged(string value)
    {
        string name = value.Trim();
        if (_settings is not null && name.Length > 0 && name != _settings.Current.StudyAuthor)
        {
            _settings.Save(_settings.Current with { StudyAuthor = name });
        }

        RebuildPins();
    }

    partial void OnFilterChanged(StudyCategoryOption value) => RebuildTimeline();

    /// <summary>The part shown on a phone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMoveSection), nameof(ShowDrawSection), nameof(ShowGameSection), nameof(IsMoveSection), nameof(IsDrawSection), nameof(IsGameSection))]
    private StudySection _section = StudySection.Move;

    /// <summary>Phones show the panel one part at a time, picked from tabs at the top.</summary>
    public bool IsSectioned => PlatformInfo.IsPhone;

    public bool ShowMoveSection => !IsSectioned || Section == StudySection.Move;

    public bool ShowDrawSection => !IsSectioned || Section == StudySection.Draw;

    public bool ShowGameSection => !IsSectioned || Section == StudySection.Game;

    public bool IsMoveSection
    {
        get => Section == StudySection.Move;
        set
        {
            if (value)
            {
                Section = StudySection.Move;
            }
        }
    }

    public bool IsDrawSection
    {
        get => Section == StudySection.Draw;
        set
        {
            if (value)
            {
                Section = StudySection.Draw;
            }
        }
    }

    public bool IsGameSection
    {
        get => Section == StudySection.Game;
        set
        {
            if (value)
            {
                Section = StudySection.Game;
            }
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (!value && (_pendingDrawings is not null || _shapeStart is not null))
        {
            DropPending();
            Sync();
        }

        _game.ShowStatus(Tr.T(value ? "Study.Opened" : "Study.Closed"));
        BoardClickCommand.NotifyCanExecuteChanged();
    }

    // ---------- Pins ----------

    /// <summary>Pins the current move; an empty note is fine (the category itself says "key moment", "mistake"…).</summary>
    [RelayCommand]
    private void AddPin()
    {
        PinCategory category = NewCategory.Category ?? PinCategory.KeyMoment;
        var pin = new StudyPin(StudyStore.NewId(), category, AuthorName, _time.GetUtcNow(), NewPinText.Trim(), []);
        Update(s => s with { Pins = [.. s.Pins, pin] });
        NewPinText = string.Empty;
        _game.ShowStatus(Tr.F("Study.PinAdded", StudyReport.CategoryName(category)));
    }

    /// <summary>Adds a pin from the board context (e.g. a key from the panel's quick buttons).</summary>
    public void AddPin(PinCategory category, string text)
    {
        NewCategory = Categories.First(c => c.Category == category);
        NewPinText = text;
        AddPin();
    }

    internal void AddReply(StudyPinRow row, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var reply = new StudyReply(AuthorName, _time.GetUtcNow(), text);
        Update(s => s with { Pins = [.. s.Pins.Select(p => p.Id == row.Pin.Id ? p with { Replies = [.. p.Replies, reply] } : p)] });
    }

    internal async Task DeletePinAsync(StudyPinRow row)
    {
        // With tap and confirm (phones), a slip of the finger must not lose a note and its replies.
        if (ConfirmEdits && _dialogs is not null && !await _dialogs.ConfirmAsync(Tr.T("Study.DeletePinTitle"), Tr.T("Study.DeletePinAsk")))
        {
            return;
        }

        Update(s => s with { Pins = [.. s.Pins.Where(p => p.Id != row.Pin.Id)] });
    }

    [RelayCommand]
    private async Task ClearDrawings()
    {
        if (ConfirmEdits && Drawings.Count > 0 && _dialogs is not null
            && !await _dialogs.ConfirmAsync(Tr.T("Study.ClearTitle"), Tr.T("Study.ClearAsk")))
        {
            return;
        }

        DropPending();
        Update(s => s with { Drawings = [] });
    }
}
