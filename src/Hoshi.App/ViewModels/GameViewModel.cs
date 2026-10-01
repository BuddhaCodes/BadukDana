using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public enum EditTool
{
    BlackStone,
    WhiteStone,
    Triangle,
    Square,
    Circle,
    Cross,
    Label,
}

/// <summary>
/// The game shown in the main window: a <see cref="GameCursor"/> over an SGF tree, with local play, navigation,
/// editing and file handling. All state shown in the UI is derived from the cursor after every change.
/// </summary>
public sealed partial class GameViewModel : ViewModelBase
{
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly ILogger<GameViewModel> _logger;
    private GameCursor _cursor = null!;
    private string? _statusOverride;

    [ObservableProperty]
    private Point? _hoverPoint;

    [ObservableProperty]
    private Stone _ghostStone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isEditMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private EditTool _editTool = EditTool.BlackStone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isDirty;

    /// <summary>Incremented on every tree change so views that render the tree know to redraw.</summary>
    [ObservableProperty]
    private int _treeVersion;

    public GameViewModel()
        : this(19, RuleSet.Japanese)
    {
    }

    public GameViewModel(int size, RuleSet rules)
        : this(null, null, null)
    {
        Load(GameTree.Create(size, rules), path: null);
    }

    public GameViewModel(IFileDialogService? files, IDialogService? dialogs, ILogger<GameViewModel>? logger)
    {
        _files = files;
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<GameViewModel>.Instance;
        Load(GameTree.Create(19, RuleSet.Japanese), path: null);
    }

    public GameCursor Cursor => _cursor;

    /// <summary>The online game shown on the board, or null for a local game.</summary>
    public OnlineGameViewModel? Online { get; private set; }

    public bool IsOnline => Online is not null;

    /// <summary>Last node of the main line (where online moves are appended).</summary>
    public GameNode MainLineEnd
    {
        get
        {
            GameNode n = Tree.Root;
            while (n.Children.Count > 0)
            {
                n = n.Children[0];
            }

            return n;
        }
    }

    public int MainLineMoveCount => GameCursor.Path(MainLineEnd).Count(n => n.HasMove);

    public GameTree Tree => _cursor.Tree;

    public GameNode CurrentNode => _cursor.Current;

    public BoardState Board => _cursor.Board;

    public Point? LastMove => _cursor.LastMove;

    public IReadOnlyList<Markup> Markers => Online is { } online
        ? [.. _cursor.Current.GetMarkup(), .. online.Overlay]
        : _cursor.Current.GetMarkup();

    public int MoveNumber => _cursor.MoveNumber;

    public string MoveNumberText => Tr.F("Game.MoveNumber", MoveNumber);

    public string CapturesText => Tr.F("Game.Captures", Board.BlackCaptures, Board.WhiteCaptures);

    public string BlackName => PlayerName(Tree.Info.BlackPlayer, Tree.Info.BlackRank, Tr.T("Common.Black"));

    public string WhiteName => PlayerName(Tree.Info.WhitePlayer, Tree.Info.WhiteRank, Tr.T("Common.White"));

    public bool IsBlackToMove => Board.ToMove == Stone.Black;

    /// <summary>Sidebar header: the game's name, else the file name, else "Nueva partida".</summary>
    public string HeaderTitle =>
        Tree.Info.GameName is { Length: > 0 } n ? n
        : FilePath is not null ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
        : Tr.T("Game.NewGame");

    /// <summary>Sidebar header details, e.g. "19×19 · reglas japonesas · komi 6.5 · B+R".</summary>
    public string HeaderSubtitle
    {
        get
        {
            var parts = new List<string> { string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Board.Width}×{Board.Height}") };
            if (Tree.Info.Rules is { } rules)
            {
                parts.Add(Tr.F("Game.Rules", rules.Name switch
                {
                    "japanese" => Tr.T("Game.Rules.Japanese"),
                    "chinese" => Tr.T("Game.Rules.Chinese"),
                    "korean" => Tr.T("Game.Rules.Korean"),
                    "aga" => "AGA",
                    "nz" => Tr.T("Game.Rules.NewZealand"),
                    "ing" => "Ing",
                    var other => other,
                }));
            }

            if (Tree.Info.Komi is { } komi)
            {
                parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"komi {komi:0.#}"));
            }

            if (Tree.Info.Handicap > 0)
            {
                parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"H{Tree.Info.Handicap}"));
            }

            if (Tree.Info.Result is { Length: > 0 } result)
            {
                parts.Add(result);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool IsGameOver =>
        _cursor.Current.GetMove(_cursor.BoardSize) is { IsPass: true }
        && _cursor.Current.Parent?.GetMove(_cursor.BoardSize) is { IsPass: true };

    public string Title => Online is { } o ? $"{o.Title} — Hoshi" :
        $"{(FilePath is null ? Tr.T("Game.Untitled") : System.IO.Path.GetFileName(FilePath))}{(IsDirty ? " *" : string.Empty)} — Hoshi";

    public string? Comment
    {
        get => _cursor.Comment;
        set
        {
            if ((_cursor.Comment ?? string.Empty) != (value ?? string.Empty))
            {
                _cursor.Comment = value; // raises Changed → Refresh
                IsDirty = true;
            }
        }
    }

    public string StatusText
    {
        get
        {
            if (_statusOverride is { } s)
            {
                return s;
            }

            if (Online is { } online)
            {
                return online.StatusText;
            }

            if (IsEditMode)
            {
                return Tr.F("Game.EditMode", ToolName(EditTool));
            }

            if (IsGameOver)
            {
                return Tr.T("Game.OverTwoPasses");
            }

            return _cursor.Current.GetMove(_cursor.BoardSize) is { IsPass: true } pass
                ? Tr.F("Game.Passed", Name(pass.Color), Tr.T(Board.ToMove == Stone.Black ? "Game.BlackToPlayAfterPass" : "Game.WhiteToPlayAfterPass"))
                : Tr.T(Board.ToMove == Stone.Black ? "Game.BlackToPlay" : "Game.WhiteToPlay");
        }
    }

    /// <summary>Replaces the game (used by New/Open and by tests).</summary>
    public void Load(GameTree tree, string? path)
    {
        if (_cursor is not null)
        {
            _cursor.Changed -= OnCursorChanged;
        }

        _cursor = new GameCursor(tree);
        _cursor.Changed += OnCursorChanged;
        _shownNode = _cursor.Current;
        FilePath = path;
        IsDirty = false;
        _statusOverride = null;
        Refresh();
    }

    // ---------- Online games (driven by OnlineGameViewModel) ----------

    /// <summary>Shows an online game: its tree replaces the current one and clicks go to OGS.</summary>
    public void LoadOnline(GameTree tree, OnlineGameViewModel online)
    {
        ArgumentNullException.ThrowIfNull(online);
        if (!ReferenceEquals(Online, online))
        {
            if (Online is not null)
            {
                Online.PropertyChanged -= OnOnlinePropertyChanged;
            }

            Online = online;
            online.PropertyChanged += OnOnlinePropertyChanged;
            IsEditMode = false;
        }

        Load(tree, path: null);
        _cursor.Last();
        OnlineChanged();
    }

    /// <summary>Stops routing to the online game; the tree stays as a normal game that can be saved.</summary>
    public void DetachOnline()
    {
        if (Online is null)
        {
            return;
        }

        Online.PropertyChanged -= OnOnlinePropertyChanged;
        Online = null;
        IsDirty = true;
        OnlineChanged();
    }

    /// <summary>Appends a move to the main line; the view follows it only if the user was at the last move.</summary>
    public void AppendOnlineMove(Stone color, Point? point)
    {
        GameNode end = MainLineEnd;
        bool follow = _cursor.Current == end;
        GameNode node = end.AddChild();
        node.SetValue(color == Stone.White ? "W" : "B", point?.ToSgf() ?? string.Empty);
        if (follow)
        {
            _cursor.GoTo(node);
        }
        else
        {
            _cursor.NotifyEdited();
        }
    }

    /// <summary>After an accepted undo: keeps the first <paramref name="moveCount"/> moves of the main line.</summary>
    public void TruncateOnlineMoves(int moveCount)
    {
        GameNode n = Tree.Root;
        int moves = 0;
        while (n.Children.Count > 0 && moves < moveCount)
        {
            n = n.Children[0];
            if (n.HasMove)
            {
                moves++;
            }
        }

        foreach (GameNode child in n.Children.ToList())
        {
            child.Detach();
        }

        _cursor.GoTo(n);
        _cursor.NotifyEdited();
    }

    /// <summary>Re-evaluates board state derived from the online game (overlay, ghost, commands, status).</summary>
    public void RefreshOnline() => Refresh();

    private void OnOnlinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnlineGameViewModel.StatusText) or nameof(OnlineGameViewModel.Title))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Title));
        }

        if (e.PropertyName is nameof(OnlineGameViewModel.IsSending) or nameof(OnlineGameViewModel.Phase))
        {
            PassCommand.NotifyCanExecuteChanged();
            UpdateGhost();
        }
    }

    private void OnlineChanged()
    {
        OnPropertyChanged(nameof(Online));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(Title));
        ToggleEditModeCommand.NotifyCanExecuteChanged();
        SelectToolCommand.NotifyCanExecuteChanged();
        Refresh();
    }

    // ---------- Play and edit ----------

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play(Point point)
    {
        if (Online is { } online)
        {
            online.OnBoardClicked(point);
            return;
        }

        if (IsEditMode)
        {
            ApplyTool(point);
            return;
        }

        MoveResult result = _cursor.Play(point);
        if (!result.IsLegal)
        {
            SetStatus(Tr.F("Game.IllegalMove", point.ToHuman(Board.Height), Describe(result.Reason)));
            return;
        }

        IsDirty = true;
        MovePlayed?.Invoke(this, CurrentNode);
    }

    /// <summary>A stone was just played on the board by the user (not navigation, not an edit, not a pass).</summary>
    public event EventHandler<GameNode>? MovePlayed;

    [RelayCommand(CanExecute = nameof(CanPass))]
    private void Pass()
    {
        if (Online is { } online)
        {
            online.PassCommand.Execute(null);
            return;
        }

        _cursor.Play(null);
        IsDirty = true;
    }

    /// <summary>The P shortcut: local games only, so a stray key press never passes in an online game.</summary>
    [RelayCommand(CanExecute = nameof(CanPassWithKey))]
    private void PassKey() => Pass();

    private bool CanPassWithKey() => Online is null && CanPass();

    /// <summary>Takes back the last move: removes it if it ends the line, otherwise just steps back.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_cursor.Current.Children.Count == 0)
        {
            _cursor.DeleteCurrent();
            IsDirty = true;
        }
        else
        {
            _cursor.Previous();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SelectTool(EditTool tool)
    {
        EditTool = tool;
        IsEditMode = true;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void DeleteNode()
    {
        _cursor.DeleteCurrent();
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void PromoteVariation()
    {
        _cursor.PromoteToMainLine();
        IsDirty = true;
    }

    // ---------- Navigation ----------

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _cursor.Previous();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _cursor.Next();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoFirst() => _cursor.First();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoLast() => _cursor.Last();

    [RelayCommand]
    private void NextVariation() => _cursor.NextVariation();

    [RelayCommand]
    private void PreviousVariation() => _cursor.PreviousVariation();

    [RelayCommand]
    private void GoToNode(GameNode? node)
    {
        if (node is not null)
        {
            _cursor.GoTo(node);
        }
    }

    /// <summary>Mouse wheel over the board: negative goes back, positive forward.</summary>
    [RelayCommand]
    private void Scroll(int direction)
    {
        if (direction < 0)
        {
            _cursor.Previous();
        }
        else
        {
            _cursor.Next();
        }
    }

    // ---------- Files ----------

    [RelayCommand]
    private async Task NewGame(int size)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        Online?.LeaveCommand.Execute(null);
        int n = size is >= BoardState.MinSize and <= BoardState.MaxSize ? size : 19;
        Load(GameTree.Create(n, Board.Rules), path: null);
    }

    [RelayCommand]
    private async Task Open()
    {
        if (_files is null || !await ConfirmDiscardAsync() || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        await OpenFileAsync(path, confirm: false);
    }

    /// <summary>Opens a file by path (drag and drop, command line). Returns false when it could not be read.</summary>
    public async Task<bool> OpenFileAsync(string path, bool confirm = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (confirm && !await ConfirmDiscardAsync())
        {
            return false;
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            SgfParseResult parsed = SgfParser.ParseCollection(data);
            if (parsed.Games.Count == 0)
            {
                throw new FormatException(Tr.T("Game.NoSgfGame"));
            }

            Online?.LeaveCommand.Execute(null);
            Load(parsed.Games[0], path);
            int warnings = parsed.Warnings.Count + _cursor.Warnings.Count;
            _logger.LogInformation("Opened {File} ({Nodes} nodes, {Warnings} warnings)",
                System.IO.Path.GetFileName(path), Tree.AllNodes().Count(), warnings);
            if (warnings > 0 || parsed.Games.Count > 1)
            {
                SetStatus(parsed.Games.Count > 1
                    ? Tr.F("Game.OpenedFirstOf", parsed.Games.Count)
                    : Tr.F("Game.OpenedWithWarnings", warnings));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not open {File}", System.IO.Path.GetFileName(path));
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Game.CouldNotOpenFile"), ex.Message);
            }

            SetStatus(Tr.T("Game.CouldNotOpenFile"));
            return false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (FilePath is null)
        {
            await SaveAs();
            return;
        }

        await SaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        string suggested = FilePath is null ? SuggestedFileName() : System.IO.Path.GetFileName(FilePath);
        if (_files is not null && await _files.PickSgfToSaveAsync(suggested) is { } path)
        {
            await SaveToAsync(path);
        }
    }

    public async Task SaveToAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await File.WriteAllBytesAsync(path, SgfWriter.WriteBytes(Tree));
        FilePath = path;
        IsDirty = false;
        SetStatus(Tr.F("Game.Saved", System.IO.Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task EditGameInfo()
    {
        if (_dialogs is null)
        {
            return;
        }

        var info = new GameInfoViewModel(Tree.Info);
        if (await _dialogs.EditGameInfoAsync(info))
        {
            ApplyGameInfo(info);
        }
    }

    public void ApplyGameInfo(GameInfoViewModel info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.ApplyTo(Tree.Info);
        IsDirty = true;
        _cursor.NotifyEdited();
    }

    // ---------- Internals ----------

    private bool CanPlay() => Online is not null || IsEditMode || !IsGameOver;

    private bool CanUndo() => Online is null && _cursor.CanGoBack;

    private bool CanEdit() => Online is null;

    private bool CanPass() => Online is { } online ? online.PassCommand.CanExecute(null) : !IsEditMode && !IsGameOver;

    private bool CanGoBack() => _cursor.CanGoBack;

    private bool CanGoForward() => _cursor.CanGoForward;

    private async Task<bool> ConfirmDiscardAsync() =>
        !IsDirty || _dialogs is null
        || await _dialogs.ConfirmAsync(Tr.T("Dialog.UnsavedChanges"), Tr.T("Game.DiscardChanges"));

    private void ApplyTool(Point point)
    {
        switch (EditTool)
        {
            case EditTool.BlackStone:
                _cursor.ToggleSetupStone(point, Stone.Black);
                break;
            case EditTool.WhiteStone:
                _cursor.ToggleSetupStone(point, Stone.White);
                break;
            case EditTool.Triangle:
                _cursor.ToggleMarkup(point, MarkupKind.Triangle);
                break;
            case EditTool.Square:
                _cursor.ToggleMarkup(point, MarkupKind.Square);
                break;
            case EditTool.Circle:
                _cursor.ToggleMarkup(point, MarkupKind.Circle);
                break;
            case EditTool.Cross:
                _cursor.ToggleMarkup(point, MarkupKind.Cross);
                break;
            case EditTool.Label:
                _cursor.ToggleMarkup(point, MarkupKind.Label);
                break;
        }

        IsDirty = true;
    }

    private void OnCursorChanged(object? sender, EventArgs e)
    {
        _statusOverride = null;
        GameNode now = _cursor.Current;
        GameNode? before = _shownNode;
        _shownNode = now;
        Refresh();

        // One stone more on the board: played here, received from OGS, or one step forward through the game.
        if (now != before && now.Parent == before && now.GetMove(_cursor.BoardSize) is { IsPass: false, Point: { } p } move)
        {
            StonePlaced?.Invoke(this, move.Color);
            BoardState was = _cursor.GetBoard(before!);
            BoardState board = _cursor.Board;
            int captured = was.AllPoints.Count(q => was[q] != Stone.Empty && board[q] == Stone.Empty);
            if (captured > 0)
            {
                StonesCaptured?.Invoke(this, captured);
            }

            IReadOnlyList<AtariGroup> atari = Atari.NewlyInAtari(was, board);
            if (atari.Count > 0)
            {
                GroupsEnteredAtari?.Invoke(this, atari);
            }
        }
    }

    /// <summary>The stone just placed left these groups (of either colour) newly in atari.</summary>
    public event EventHandler<IReadOnlyList<AtariGroup>>? GroupsEnteredAtari;

    /// <summary>The stone just placed captured this many stones.</summary>
    public event EventHandler<int>? StonesCaptured;

    private GameNode? _shownNode;

    /// <summary>A stone appeared one move after the previous position (for the placement sound).</summary>
    public event EventHandler<Stone>? StonePlaced;

    private void SetStatus(string text)
    {
        _statusOverride = text;
        OnPropertyChanged(nameof(StatusText));
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Board));
        OnPropertyChanged(nameof(LastMove));
        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(CurrentNode));
        OnPropertyChanged(nameof(Tree));
        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(MoveNumber));
        OnPropertyChanged(nameof(MoveNumberText));
        OnPropertyChanged(nameof(CapturesText));
        OnPropertyChanged(nameof(BlackName));
        OnPropertyChanged(nameof(WhiteName));
        OnPropertyChanged(nameof(IsBlackToMove));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(IsGameOver));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(MainLineMoveCount));
        TreeVersion++;

        PlayCommand.NotifyCanExecuteChanged();
        PassCommand.NotifyCanExecuteChanged();
        PassKeyCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        DeleteNodeCommand.NotifyCanExecuteChanged();
        PromoteVariationCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoFirstCommand.NotifyCanExecuteChanged();
        GoLastCommand.NotifyCanExecuteChanged();
        UpdateGhost();
    }

    partial void OnHoverPointChanged(Point? value) => UpdateGhost();

    partial void OnIsEditModeChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        PassCommand.NotifyCanExecuteChanged();
        UpdateGhost();
    }

    partial void OnEditToolChanged(EditTool value) => UpdateGhost();

    private void UpdateGhost()
    {
        if (HoverPoint is not { } p || !Board.IsOnBoard(p))
        {
            GhostStone = Stone.Empty;
        }
        else if (Online is { } online)
        {
            GhostStone = online.CanPlayAt(p) ? online.MyColor : Stone.Empty;
        }
        else if (IsEditMode)
        {
            GhostStone = EditTool switch
            {
                EditTool.BlackStone when Board[p] == Stone.Empty => Stone.Black,
                EditTool.WhiteStone when Board[p] == Stone.Empty => Stone.White,
                _ => Stone.Empty,
            };
        }
        else
        {
            GhostStone = !IsGameOver && Board.IsLegal(Board.ToMove, p) ? Board.ToMove : Stone.Empty;
        }
    }

    private string SuggestedFileName()
    {
        string b = Tree.Info.BlackPlayer ?? Tr.T("Common.Black");
        string w = Tree.Info.WhitePlayer ?? Tr.T("Common.White");
        string name = $"{b} vs {w}.sgf";
        return string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    private static string PlayerName(string? name, string? rank, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : string.IsNullOrWhiteSpace(rank) ? name : $"{name} {rank}";

    private static string Name(Stone color) => Tr.T(color == Stone.Black ? "Common.Black" : "Common.White");

    private static string ToolName(EditTool tool) => tool switch
    {
        EditTool.BlackStone => Tr.T("Game.Tool.BlackStone"),
        EditTool.WhiteStone => Tr.T("Game.Tool.WhiteStone"),
        EditTool.Triangle => Tr.T("Game.Tool.Triangle"),
        EditTool.Square => Tr.T("Game.Tool.Square"),
        EditTool.Circle => Tr.T("Game.Tool.Circle"),
        EditTool.Cross => Tr.T("Game.Tool.Cross"),
        _ => Tr.T("Game.Tool.Label"),
    };

    private static string Describe(IllegalMoveReason? reason) => reason switch
    {
        IllegalMoveReason.Occupied => Tr.T("Game.Illegal.Occupied"),
        IllegalMoveReason.Suicide => Tr.T("Game.Illegal.Suicide"),
        IllegalMoveReason.Ko => "ko",
        IllegalMoveReason.Superko => Tr.T("Game.Illegal.Superko"),
        IllegalMoveReason.OutOfBounds => Tr.T("Game.Illegal.OutOfBounds"),
        _ => Tr.T("Game.Illegal.Other"),
    };
}
