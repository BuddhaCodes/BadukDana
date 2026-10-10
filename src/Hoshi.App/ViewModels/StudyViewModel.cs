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
    private void Delete() => _owner.DeletePin(this);
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
        : IsActive ? Drawings : [];

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

    partial void OnIsActiveChanged(bool value)
    {
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

    internal void DeletePin(StudyPinRow row) =>
        Update(s => s with { Pins = [.. s.Pins.Where(p => p.Id != row.Pin.Id)] });

    [RelayCommand]
    private void ClearDrawings() => Update(s => s with { Drawings = [] });

    [RelayCommand]
    private void PreviousPin() => GoToPin(forward: false);

    [RelayCommand]
    private void NextPin() => GoToPin(forward: true);

    [RelayCommand]
    private void GoToMark(TimelineMark? mark)
    {
        if (mark is not null)
        {
            _game.GoToNodeCommand.Execute(mark.Node);
        }
    }

    private void GoToPin(bool forward)
    {
        IReadOnlyList<GameNode> track = Track();
        int here = track.ToList().IndexOf(_game.CurrentNode);
        IEnumerable<GameNode> candidates = forward ? track.Skip(here + 1) : track.Take(Math.Max(0, here)).Reverse();
        if (candidates.FirstOrDefault(n => Matches(StudyStore.Read(n))) is { } target)
        {
            _game.GoToNodeCommand.Execute(target);
        }
        else
        {
            _game.ShowStatus(Tr.T(forward ? "Study.NoNextPin" : "Study.NoPreviousPin"));
        }
    }

    // ---------- Board ----------

    /// <summary>A click on the board: the quiz's answer, a study tool, or the normal move.</summary>
    [RelayCommand(CanExecute = nameof(CanBoardClick))]
    private void BoardClick(Point point)
    {
        if (IsQuizActive)
        {
            AnswerQuiz(point);
            return;
        }

        if (IsActive && Tool != StudyTool.Note)
        {
            ApplyTool(point);
            return;
        }

        if (_game.PlayCommand.CanExecute(point))
        {
            _game.PlayCommand.Execute(point);
        }
    }

    private bool CanBoardClick(Point point) => IsQuizActive || (IsActive && Tool != StudyTool.Note) || _game.PlayCommand.CanExecute(point);

    /// <summary>A drag on the board: an arrow or an area.</summary>
    [RelayCommand]
    private void BoardDrag(BoardDrag? drag)
    {
        if (drag is null || !IsActive || IsQuizActive || !_game.Board.IsOnBoard(drag.From) || !_game.Board.IsOnBoard(drag.To))
        {
            return;
        }

        DrawingKind? kind = Tool switch
        {
            StudyTool.Arrow => DrawingKind.Arrow,
            StudyTool.Area => DrawingKind.Area,
            _ => null,
        };
        if (kind is { } k)
        {
            Add(new StudyDrawing(StudyStore.NewId(), k, AuthorName, Color, [drag.From, drag.To]));
        }
    }

    private void ApplyTool(Point point)
    {
        if (!_game.Board.IsOnBoard(point))
        {
            return;
        }

        NodeStudy study = StudyStore.Read(_game.CurrentNode);
        switch (Tool)
        {
            case StudyTool.Mark:
                if (study.Drawings.FirstOrDefault(d => d.Kind == DrawingKind.Mark && d.Points[0] == point) is { } mark)
                {
                    Remove(mark.Id);
                }
                else
                {
                    Add(new StudyDrawing(StudyStore.NewId(), DrawingKind.Mark, AuthorName, Color, [point]));
                }

                break;
            case StudyTool.Label:
                if (study.Drawings.FirstOrDefault(d => d.Kind == DrawingKind.Label && d.Points[0] == point) is { } label)
                {
                    Remove(label.Id);
                }
                else
                {
                    Add(new StudyDrawing(StudyStore.NewId(), DrawingKind.Label, AuthorName, Color, [point], NextLetter(study)));
                }

                break;
            case StudyTool.Sequence:
                ExtendSequence(study, point);
                break;
            case StudyTool.Erase:
                string[] hit = [.. study.Drawings.Where(d => Touches(d, point)).Select(d => d.Id)];
                if (hit.Length > 0)
                {
                    Update(s => s with { Drawings = [.. s.Drawings.Where(d => !hit.Contains(d.Id))] });
                }

                break;
            case StudyTool.Arrow or StudyTool.Area:
                _game.ShowStatus(Tr.T("Study.DragToDraw"));
                break;
        }
    }

    private void ExtendSequence(NodeStudy study, Point point)
    {
        StudyDrawing? current = _sequenceId is { } id ? study.Drawings.FirstOrDefault(d => d.Id == id) : null;
        if (current is not null && current.Points.Count > 0 && current.Points[^1] == point)
        {
            // Clicking the last stone takes it back (the whole line when it was the only one).
            if (current.Points.Count == 1)
            {
                Remove(current.Id);
                _sequenceId = null;
            }
            else
            {
                Replace(current with { Points = [.. current.Points.Take(current.Points.Count - 1)] });
            }

            return;
        }

        if (_game.Board[point] != Stone.Empty || current?.Points.Contains(point) == true)
        {
            return;
        }

        if (current is null)
        {
            Stone first = SequenceStart(study);
            var line = new StudyDrawing(StudyStore.NewId(), DrawingKind.Sequence, AuthorName, Color, [point], first == Stone.White ? "W" : "B");
            _sequenceId = line.Id;
            Add(line);
        }
        else
        {
            Replace(current with { Points = [.. current.Points, point] });
        }
    }

    private Stone NextSequenceColor()
    {
        NodeStudy study = StudyStore.Read(_game.CurrentNode);
        if (_sequenceId is { } id && study.Drawings.FirstOrDefault(d => d.Id == id) is { } line)
        {
            return line.Points.Count % 2 == 0 ? line.FirstColor : line.FirstColor.Opponent();
        }

        return SequenceStart(study);
    }

    /// <summary>
    /// The first colour of a new line: the side to move, except on a move pinned as a mistake, where the line shows
    /// what that player should have played instead (and the quiz takes it as the answer).
    /// </summary>
    private Stone SequenceStart(NodeStudy study) =>
        study.Pins.Any(p => p.Category == PinCategory.Mistake) && _game.CurrentNode.GetMove(_game.Cursor.BoardSize) is { } m
            ? m.Color
            : _game.Board.ToMove;

    private static bool Touches(StudyDrawing d, Point p) => d.Kind switch
    {
        DrawingKind.Area => d.AreaPoints().Contains(p),
        _ => d.Points.Contains(p),
    };

    private static string NextLetter(NodeStudy study)
    {
        var used = study.Drawings.Where(d => d.Kind == DrawingKind.Label).Select(d => d.Text).ToHashSet();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!used.Contains(c.ToString()))
            {
                return c.ToString();
            }
        }

        return "?";
    }

    private void Add(StudyDrawing drawing) => Update(s => s with { Drawings = [.. s.Drawings, drawing] });

    private void Remove(string id) => Update(s => s with { Drawings = [.. s.Drawings.Where(d => d.Id != id)] });

    private void Replace(StudyDrawing drawing) => Update(s => s with { Drawings = [.. s.Drawings.Select(d => d.Id == drawing.Id ? drawing : d)] });

    /// <summary>Changes the study of the current move, saves it in the tree and refreshes everything.</summary>
    private void Update(Func<NodeStudy, NodeStudy> change)
    {
        GameNode node = _game.CurrentNode;
        StudyStore.Write(node, change(StudyStore.Read(node)));
        _game.StudyEdited();
        Sync();
    }

    // ---------- Sharing ----------

    /// <summary>Saves the game with its study as an SGF that Hoshi reopens and other programs can read.</summary>
    [RelayCommand]
    private async Task SaveStudy()
    {
        if (_files is null || await _files.PickSgfToSaveAsync(SuggestedName(Tr.T("Study.FileSuffix"), "sgf")) is not { } path)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, StudyStore.WriteWithMirror(_game.Tree), new UTF8Encoding(false));
            _game.ShowStatus(Tr.F("Study.Saved", Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the study");
            await ShowError(Tr.T("Study.CouldNotSave"), ex.Message);
        }
    }

    /// <summary>Opens a study file: its notes are added to this game (same moves), or it opens as a new game.</summary>
    [RelayCommand]
    private async Task OpenStudy()
    {
        if (_files is null || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        await OpenStudyFileAsync(path);
    }

    public async Task OpenStudyFileAsync(string path)
    {
        GameTree source;
        try
        {
            SgfParseResult parsed = SgfParser.ParseCollection(await File.ReadAllBytesAsync(path));
            if (parsed.Games.Count == 0)
            {
                throw new FormatException(Tr.T("Game.NoSgfGame"));
            }

            source = parsed.Games[0];
            StudyStore.StripMirror(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not open the study {File}", Path.GetFileName(path));
            await ShowError(Tr.T("Game.CouldNotOpenFile"), ex.Message);
            return;
        }

        bool emptyHere = !_game.Tree.Root.Children.Any() && !_game.IsOnline;
        bool sameGame = SameGame(_game.Tree, source);
        if (emptyHere || (!sameGame && !_game.IsOnline && _dialogs is not null
            && await _dialogs.ConfirmAsync(Tr.T("Study.OtherGameTitle"), Tr.T("Study.OtherGame"))))
        {
            if (await _game.OpenFileAsync(path, confirm: !emptyHere))
            {
                IsActive = true;
            }

            return;
        }

        if (!sameGame)
        {
            _game.ShowStatus(Tr.T("Study.NotThisGame"));
            return;
        }

        StudyMergeResult result = StudyStore.Merge(_game.Tree, source, createMissingMoves: !_game.IsOnline);
        if (result.NewMoves > 0)
        {
            _game.Cursor.NotifyEdited();
        }

        _game.StudyEdited();
        IsActive = true;
        Sync();
        _game.ShowStatus(result.Items == 0
            ? Tr.T("Study.NothingNew")
            : Tr.F("Study.Merged", result.Items, Path.GetFileName(path)));
    }

    /// <summary>The same game when the first moves agree (up to the shorter of the two, at most 20).</summary>
    internal static bool SameGame(GameTree a, GameTree b)
    {
        if (a.Info.Width != b.Info.Width || a.Info.Height != b.Info.Height)
        {
            return false;
        }

        int size = Math.Max(a.Info.Width, a.Info.Height);
        var ma = MainLine(a).Select(n => n.GetMove(size)).Where(m => m is not null).Take(20).ToList();
        var mb = MainLine(b).Select(n => n.GetMove(size)).Where(m => m is not null).Take(20).ToList();
        int n = Math.Min(ma.Count, mb.Count);
        return n == 0 ? ma.Count == mb.Count || ma.Count == 0 : ma.Take(n).SequenceEqual(mb.Take(n));
    }

    private static IEnumerable<GameNode> MainLine(GameTree tree)
    {
        for (GameNode n = tree.Root; ; n = n.Children[0])
        {
            yield return n;
            if (n.Children.Count == 0)
            {
                yield break;
            }
        }
    }

    /// <summary>The game report (a web page): the pins, the drawings and, when allowed, KataGo's verdict on each moment.</summary>
    [RelayCommand]
    private async Task Report()
    {
        if (_files is null || await _files.PickHtmlToSaveAsync(SuggestedName(Tr.T("Study.ReportSuffix"), "html")) is not { } path)
        {
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<GameNode, StudyVerdict>? verdicts = null;
            if (_analysis is { IsEngineActive: true } analysis)
            {
                _game.ShowStatus(Tr.T("Study.ReportAnalysing"));
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var nodes = StudyStore.All(_game.Tree).Where(x => x.Study.Pins.Count > 0).Select(x => x.Node).ToList();
                verdicts = await analysis.VerdictsAsync(nodes, cts.Token);
            }

            await File.WriteAllTextAsync(path, StudyReport.Html(_game.Tree, verdicts, _time.GetLocalNow()), new UTF8Encoding(false));
            _game.ShowStatus(Tr.F("Study.ReportSaved", Path.GetFileName(path)));
            if (_browser is not null)
            {
                await _browser.OpenAsync(new Uri(Path.GetFullPath(path)), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            _game.ShowStatus(Tr.T("Study.ReportTimeout"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the report");
            await ShowError(Tr.T("Study.CouldNotSave"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string SuggestedName(string suffix, string extension)
    {
        string b = _game.Tree.Info.BlackPlayer ?? Tr.T("Common.Black");
        string w = _game.Tree.Info.WhitePlayer ?? Tr.T("Common.White");
        string name = $"{b} vs {w} — {suffix}.{extension}";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private Task ShowError(string title, string message) => _dialogs?.ShowErrorAsync(title, message) ?? Task.CompletedTask;

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
            QuizResult = Tr.F("Study.QuizDone", QuizRight, QuizDone);
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

        QuizResult = Tr.T(right ? "Study.QuizSelfRight" : "Study.QuizSelfWrong");
    }

    [RelayCommand]
    private void EndQuiz()
    {
        IsQuizActive = false;
        IsQuizFinished = false;
        Question = null;
        QuizOverlay = [];
        QuizResult = string.Empty;
        BoardClickCommand.NotifyCanExecuteChanged();
    }

    private void Ask(int index)
    {
        _quizIndex = index;
        QuizQuestion q = _quiz[index];
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

    // ---------- Sync with the game ----------

    private string AuthorName => Author.Trim() is { Length: > 0 } a ? a : Tr.T("Study.Me");

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameViewModel.TreeVersion):
                Sync();
                break;
            case nameof(GameViewModel.GhostStone) or nameof(GameViewModel.HoverPoint):
                OnPropertyChanged(nameof(GhostStone));
                break;
            case nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline):
                OnPropertyChanged(nameof(IsLiveOnline));
                StartQuizCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    /// <summary>Reads the current move's study, the timeline and, for a new game, whether to open the panel.</summary>
    private void Sync()
    {
        if (!ReferenceEquals(_tree, _game.Tree))
        {
            bool first = _tree is null;
            _tree = _game.Tree;
            _sequenceId = null;
            if (IsQuizActive)
            {
                EndQuiz();
            }

            // A game that comes with a study (a shared file, a replay, a reloaded online game) opens the panel.
            if (!first && StudyStore.All(_tree).Any())
            {
                IsActive = true;
            }
        }

        if (!ReferenceEquals(_node, _game.CurrentNode))
        {
            _node = _game.CurrentNode;
            _sequenceId = null;
        }

        NodeStudy study = StudyStore.Read(_game.CurrentNode);
        Drawings = study.Drawings;
        RebuildPins(study);
        RebuildTimeline();
        OnPropertyChanged(nameof(MoveTitle));
        OnPropertyChanged(nameof(GhostStone));
        OnPropertyChanged(nameof(IsLiveOnline));
    }

    private void RebuildPins(NodeStudy? study = null)
    {
        study ??= StudyStore.Read(_game.CurrentNode);
        Pins = [.. study.Pins.Select(p => new StudyPinRow(this, p))];
        OnPropertyChanged(nameof(HasPins));
    }

    private void RebuildTimeline()
    {
        IReadOnlyList<GameNode> track = Track();
        int moves = 0;
        int current = 0;
        var marks = new List<TimelineMark>();
        foreach (GameNode n in track)
        {
            if (n.HasMove)
            {
                moves++;
            }

            if (n == _game.CurrentNode)
            {
                current = moves;
            }

            NodeStudy s = StudyStore.Read(n);
            if (s.IsEmpty || !Matches(s))
            {
                continue;
            }

            if (s.Pins.Count == 0)
            {
                marks.Add(new TimelineMark(n, moves, null, $"{DescribeNode(n)} · {Tr.T("Study.DrawingsOnly")}"));
                continue;
            }

            foreach (StudyPin p in s.Pins.Where(p => Filter.Category is null || p.Category == Filter.Category))
            {
                string text = p.Text.Length > 60 ? p.Text[..57] + "…" : p.Text;
                marks.Add(new TimelineMark(n, moves, p.Category, $"{DescribeNode(n)} · {StudyReport.CategoryName(p.Category)}{(text.Length > 0 ? ": " + text : string.Empty)}"));
            }
        }

        TimelineTotal = Math.Max(1, moves);
        TimelineCurrent = current;
        Timeline = marks;
        OnPropertyChanged(nameof(TimelineText));
    }

    private bool Matches(NodeStudy study) =>
        !study.IsEmpty && (Filter.Category is not { } c ? true : study.Pins.Any(p => p.Category == c));

    /// <summary>Root → current move → on along the first variations: the line the timeline shows.</summary>
    private IReadOnlyList<GameNode> Track()
    {
        var track = GameCursor.Path(_game.CurrentNode).ToList();
        for (GameNode n = _game.CurrentNode; n.Children.Count > 0;)
        {
            n = n.Children[0];
            track.Add(n);
        }

        return track;
    }

    private string DescribeNode(GameNode node)
    {
        int number = GameCursor.Path(node).Count(n => n.HasMove);
        if (node.GetMove(_game.Cursor.BoardSize) is not { } m)
        {
            return number == 0 ? Tr.T("Study.Start") : Tr.F("Study.AfterMove", number);
        }

        string colour = Tr.T(m.Color == Stone.Black ? "Common.Black" : "Common.White");
        string where = m.Point is { } p ? p.ToHuman(_game.Board.Height) : Tr.T("Game.PassNoun");
        return Tr.F("Study.MoveName", number, colour, where);
    }

    internal static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.GetCultureInfo(Tr.Language == Tr.Spanish ? "es-ES" : "en-GB"));
}
