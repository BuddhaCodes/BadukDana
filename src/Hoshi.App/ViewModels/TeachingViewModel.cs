using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Ogs;
using Hoshi.Ogs.Reviews;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Teacher mode through OGS review rooms. Teaching: Hoshi opens a review of the OGS game on the board and every move
/// the teacher shows — with its comment, notes and marks — appears for the students in that room (in their
/// browser, the OGS app, or Hoshi). Following: a student pastes the room's link and Hoshi's board follows the
/// teacher. Never on the player's own live OGS game (OGS does not allow reviewing it either, and no help is given
/// during it). Nothing is sent unless the teacher starts the room.
/// </summary>
public sealed partial class TeachingViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly IOgsClient? _ogs;
    private readonly Func<long?> _gameId;
    private readonly IUiDispatcher _ui;
    private readonly IClipboardService? _clipboard;
    private readonly ILogger _logger;
    private IOgsReviewRoom? _room;
    private long? _followedGame;
    private (int From, string Moves, string Text, string Marks)? _lastShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInRoom), nameof(CanTeach))]
    [NotifyCanExecuteChangedFor(nameof(StartTeachingCommand), nameof(FollowCommand))]
    private bool _isTeaching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInRoom), nameof(CanTeach))]
    [NotifyCanExecuteChangedFor(nameof(StartTeachingCommand), nameof(FollowCommand))]
    private bool _isFollowing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTeachingCommand), nameof(FollowCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FollowCommand))]
    private string _joinText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoomLink), nameof(HasRoom))]
    private long? _reviewId;

    /// <param name="game">The board.</param>
    /// <param name="ogs">The OGS connection.</param>
    /// <param name="gameId">The OGS game on the board, when it is one (null otherwise).</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="clipboard">For "Copy the link".</param>
    /// <param name="logger">Logs (never tokens).</param>
    public TeachingViewModel(GameViewModel game, IOgsClient? ogs, Func<long?> gameId, IUiDispatcher? ui = null, IClipboardService? clipboard = null, ILogger<TeachingViewModel>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _ogs = ogs;
        _gameId = gameId ?? throw new ArgumentNullException(nameof(gameId));
        _ui = ui ?? new ImmediateUiDispatcher();
        _clipboard = clipboard;
        _logger = logger ?? NullLogger<TeachingViewModel>.Instance;
        _game.PropertyChanged += OnGameChanged;
        if (_ogs is not null)
        {
            _ogs.SessionChanged += (_, _) => _ui.Post(Refresh);
        }
    }

    public bool IsInRoom => IsTeaching || IsFollowing;

    public bool HasRoom => ReviewId is not null;

    /// <summary>The room's page, for the students (their browser or the OGS app).</summary>
    public string RoomLink => ReviewId is { } id && _ogs is not null ? OgsLinks.ReviewUri(_ogs.Options.BaseUrl, id).ToString() : string.Empty;

    /// <summary>Signed in, an OGS game on the board, and not the player's own live game.</summary>
    public bool CanTeach => _ogs?.Session is not null && !IsInRoom && !IsBusy && !IsLiveGame && _gameId() is not null;

    private bool IsLiveGame => _game.Online is { IsPlayer: true, IsFinished: false };

    /// <summary>Opens a review room of the OGS game on the board and starts showing it there.</summary>
    [RelayCommand(CanExecute = nameof(CanTeach))]
    private async Task StartTeaching()
    {
        if (_ogs is null || _gameId() is not { } gameId || IsLiveGame)
        {
            return;
        }

        IsBusy = true;
        try
        {
            long id = await _ogs.CreateReviewAsync(gameId, CancellationToken.None);
            Open(id);
            IsTeaching = true;
            _lastShown = null;
            Broadcast();
            Status = Tr.T("Teach.Teaching");
            _game.ShowStatus(Status);
        }
        catch (Exception ex) when (ex is OgsApiException or HttpRequestException or InvalidOperationException or NotSupportedException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not open a review of game {GameId}", gameId);
            Status = Tr.F("Teach.Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanFollow() => _ogs?.Session is not null && !IsInRoom && !IsBusy && !IsLiveGame && OgsLinks.TryReviewId(JoinText) is not null;

    /// <summary>Joins the room of a pasted link: the board follows what the teacher shows.</summary>
    [RelayCommand(CanExecute = nameof(CanFollow))]
    private void Follow()
    {
        if (_ogs is null || OgsLinks.TryReviewId(JoinText) is not { } id || IsLiveGame)
        {
            return;
        }

        try
        {
            _followedGame = null;
            Open(id);
            IsFollowing = true;
            Status = Tr.T("Teach.Joining");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Status = Tr.F("Teach.Failed", ex.Message);
        }
    }

    [RelayCommand]
    private void Leave()
    {
        _room?.Dispose();
        _room = null;
        ReviewId = null;
        IsTeaching = false;
        IsFollowing = false;
        Status = Tr.T("Teach.Left");
    }

    [RelayCommand]
    private async Task CopyLink()
    {
        if (_clipboard is not null && RoomLink.Length > 0)
        {
            await _clipboard.SetTextAsync(RoomLink);
            _game.ShowStatus(Tr.T("Teach.LinkCopied"));
        }
    }

    private void Open(long id)
    {
        IOgsReviewRoom room = _ogs!.OpenReview(id);
        room.FullState += (_, log) => _ui.Post(() => OnFullState(room, log));
        room.Appended += (_, m) => _ui.Post(() => OnAppended(room, m));
        room.Connect();
        _room = room;
        ReviewId = id;
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CanTeach));
        StartTeachingCommand.NotifyCanExecuteChanged();
        FollowCommand.NotifyCanExecuteChanged();
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameViewModel.CurrentNode) or nameof(GameViewModel.TreeVersion):
                if (IsTeaching)
                {
                    Broadcast();
                }

                break;
            case nameof(GameViewModel.Online):
                if (IsLiveGame && IsInRoom)
                {
                    Leave(); // the player's own game started: teaching waits
                }

                Refresh();
                break;
        }

        if (e.PropertyName == nameof(GameViewModel.TreeVersion))
        {
            Refresh();
        }
    }

    // ---------- Teaching: the position on the board → the room ----------

    private void Broadcast()
    {
        if (_room is not { } room || !IsTeaching)
        {
            return;
        }

        (int from, IReadOnlyList<Point?> moves) = Locate(_game.CurrentNode, _game.Cursor.BoardSize);
        string text = TextOf(_game.CurrentNode);
        string packed = OgsReviewSession.Encode(moves);
        IReadOnlyDictionary<string, IReadOnlyList<Point>> marks = MarksOf(_game.CurrentNode);
        string marksKey = string.Join(";", marks.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key + "=" + OgsReviewSession.Encode([.. m.Value.Select(p => (Point?)p)])));
        if (_lastShown == (from, packed, text, marksKey))
        {
            return; // nothing new to show
        }

        _lastShown = (from, packed, text, marksKey);
        room.Show(from, moves, text, marks);
    }

    /// <summary>
    /// Where a node is for OGS: the move number where its line leaves the game's main line, and the moves after it.
    /// </summary>
    public static (int From, IReadOnlyList<Point?> Moves) Locate(GameNode node, int boardSize)
    {
        ArgumentNullException.ThrowIfNull(node);
        IReadOnlyList<GameNode> path = GameCursor.Path(node);
        int from = 0, i = 1;
        for (; i < path.Count && path[i - 1].Children[0] == path[i]; i++)
        {
            from += path[i].HasMove ? 1 : 0;
        }

        var moves = new List<Point?>();
        for (; i < path.Count; i++)
        {
            if (path[i].GetMove(boardSize) is { } m)
            {
                moves.Add(m.Point);
            }
        }

        return (from, moves);
    }

    /// <summary>The node's comment and its study notes (with authors), as the room's text for that move.</summary>
    public static string TextOf(GameNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        IEnumerable<string> notes = StudyStore.Read(node).Pins.Select(p => (p.Text.Length > 0 ? p.Text : Services.Study.StudyReport.CategoryName(p.Category)) + " — " + p.Author);
        return string.Join("\n", new[] { node.Comment ?? string.Empty }.Concat(notes).Where(t => t.Length > 0));
    }

    /// <summary>Study marks, letters and areas as OGS marks (circles, letters, squares).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Point>> MarksOf(GameNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var marks = new Dictionary<string, List<Point>>();
        void Add(string kind, IEnumerable<Point> points)
        {
            if (!marks.TryGetValue(kind, out List<Point>? list))
            {
                marks[kind] = list = [];
            }

            list.AddRange(points.Where(p => !list.Contains(p)));
        }

        foreach (StudyDrawing d in StudyStore.Read(node).Drawings)
        {
            switch (d.Kind)
            {
                case DrawingKind.Mark:
                    Add("circle", d.Points);
                    break;
                case DrawingKind.Area:
                    Add("square", d.AreaPoints());
                    break;
                case DrawingKind.Label when d.Text is { Length: 1 } letter && char.IsLetterOrDigit(letter[0]):
                    Add(letter, d.Points);
                    break;
                case DrawingKind.Sequence:
                    Add("triangle", d.Points.Take(1)); // where the "what if" line starts
                    break;
            }
        }

        return marks.ToDictionary(k => k.Key, k => (IReadOnlyList<Point>)k.Value);
    }

    // ---------- Following: the room → the board ----------

    private void OnFullState(IOgsReviewRoom room, IReadOnlyList<OgsReviewMessage> log)
    {
        if (!ReferenceEquals(room, _room) || !IsFollowing)
        {
            return;
        }

        if (log.Select(m => m.GameId).LastOrDefault(g => g is not null) is { } gameId && gameId != _followedGame)
        {
            _ = LoadGameThenFollow(room, gameId, log);
            return;
        }

        Apply(log.LastOrDefault(m => m.From is not null && m.Moves is not null));
    }

    private async Task LoadGameThenFollow(IOgsReviewRoom room, long gameId, IReadOnlyList<OgsReviewMessage> log)
    {
        try
        {
            string sgf = await _ogs!.GetGameSgfAsync(gameId, CancellationToken.None);
            if (!ReferenceEquals(room, _room))
            {
                return;
            }

            if (await _game.OpenSgfTextAsync(sgf, $"OGS #{gameId}"))
            {
                _followedGame = gameId;
                Status = Tr.F("Teach.Following", gameId);
                Apply(log.LastOrDefault(m => m.From is not null && m.Moves is not null));
            }
        }
        catch (Exception ex) when (ex is OgsApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not load the reviewed game {GameId}", gameId);
            Status = Tr.F("Teach.Failed", ex.Message);
        }
    }

    private void OnAppended(IOgsReviewRoom room, OgsReviewMessage message)
    {
        if (ReferenceEquals(room, _room) && IsFollowing && _followedGame is not null)
        {
            Apply(message);
        }
    }

    /// <summary>Goes to the shown position: the game's main line to the move, then the line (added as a variation).</summary>
    private void Apply(OgsReviewMessage? message)
    {
        if (message is not { From: { } from, Moves: { } moves } || IsLiveGame)
        {
            return;
        }

        GameNode node = _game.Tree.Root;
        for (int n = 0; n < from && node.Children.Count > 0;)
        {
            node = node.Children[0];
            n += node.HasMove ? 1 : 0;
        }

        _game.GoToNodeCommand.Execute(node);
        foreach (Point? move in moves)
        {
            if (!_game.Cursor.Play(move).IsLegal)
            {
                break;
            }
        }

        _game.GoToNodeCommand.Execute(_game.Cursor.Current);
        if (message.Text is { Length: > 0 } text)
        {
            Status = text;
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
