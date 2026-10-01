using System.ComponentModel;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Joseki;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Ogs.Joseki;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>A joseki continuation drawn on the board. Local-library moves have <see cref="JosekiCategory.Unknown"/>.</summary>
public sealed record BoardJosekiHint(Point Point, JosekiCategory Category, bool IsLocal)
{
    public bool IsRecommended => IsLocal || Category is JosekiCategory.Ideal or JosekiCategory.Good;

    /// <summary>The explorer's own palette, slightly softened for the wood; local lines are violet.</summary>
    public static Color ColorOf(JosekiCategory category) => category switch
    {
        JosekiCategory.Ideal => Color.FromArgb(0xE6, 0x1E, 0x9E, 0x3A),
        JosekiCategory.Good => Color.FromArgb(0xE0, 0x6E, 0x9A, 0x1E),
        JosekiCategory.Mistake => Color.FromArgb(0xD8, 0xC6, 0x28, 0x3C),
        JosekiCategory.Trick => Color.FromArgb(0xE0, 0xE8, 0xB4, 0x14),
        JosekiCategory.Question => Color.FromArgb(0xD0, 0x1E, 0xAE, 0xDB),
        _ => Color.FromArgb(0xE0, 0x8E, 0x6C, 0xE8),
    };
}

/// <summary>
/// "Natural" joseki: while you play on the main board, each corner's local sequence is looked up in your library
/// and in the OGS Joseki Explorer, and the known continuations are drawn as coloured discs (green ideal, olive
/// good, yellow trick, blue open question, red mistake; violet = your library). Off during your live OGS games.
/// </summary>
public sealed partial class JosekiAssistantViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly IJosekiLibrary? _library;
    private readonly JosekiExplorer? _explorer;
    private readonly ISettingsService? _settings;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private bool _reportedFailure;
    private string? _textKey;
    private object?[] _textArgs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private bool _isOn;

    [ObservableProperty]
    private IReadOnlyList<BoardJosekiHint>? _hints;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private bool _isSearching;

    public JosekiAssistantViewModel(
        GameViewModel game,
        IJosekiLibrary? library = null,
        JosekiExplorer? explorer = null,
        ISettingsService? settings = null,
        ILogger<JosekiAssistantViewModel>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _library = library;
        _explorer = explorer;
        _settings = settings;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _isOn = settings?.Current.JosekiHints ?? false;
        _game.PropertyChanged += OnGamePropertyChanged;
        if (_library is not null)
        {
            _library.Changed += (_, _) => _ = RefreshAsync();
        }

        if (_isOn)
        {
            _ = RefreshAsync();
        }
    }

    public bool IsBlocked => _game.Online is { IsPlayer: true, IsFinished: false };

    public bool IsActive => IsOn && !IsBlocked;

    public bool HasExplorer => _explorer is not null;

    /// <summary>One line about the corner of the last move, shown over the board.</summary>
    public string? Text => !IsActive || _textKey is null ? null : Tr.F(_textKey, _textArgs);

    public bool HasText => Text is not null;

    /// <summary>The task of the latest refresh (tests await it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        if (_settings is not null)
        {
            _settings.Save(_settings.Current with { JosekiHints = IsOn });
        }
    }

    partial void OnIsOnChanged(bool value) => _ = RefreshAsync();

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.TreeVersion) or nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline))
        {
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(IsActive));
            _ = RefreshAsync();
        }
    }

    private Task RefreshAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (!IsActive)
        {
            Hints = null;
            Description = null;
            IsSearching = false;
            SetText(null);
            return Pending = Task.CompletedTask;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        return Pending = ComputeAsync(cts.Token);
    }

    private async Task ComputeAsync(CancellationToken token)
    {
        GameCursor cursor = _game.Cursor;
        int size = cursor.BoardSize;
        BoardState board = _game.Board;
        Stone toMove = board.ToMove;
        List<(Stone, Point?)> moves = [.. GameCursor.Path(cursor.Current)
            .Select(n => n.GetMove(size))
            .Where(m => m is not null)
            .Select(m => (m!.Value.Color, m.Value.Point))];
        Point? last = moves.Count > 0 ? moves[^1].Item2 : null;
        Corner? lastCorner = last is { } lp ? Corners.Nearest(lp, size) : null;

        var hints = new Dictionary<Point, BoardJosekiHint>();
        IReadOnlyList<JosekiLine> lines = _library?.Lines ?? [];
        var sequences = new Dictionary<Corner, IReadOnlyList<CornerMove>>();
        foreach (Corner corner in Corners.All)
        {
            List<CornerMove> seq = [.. Corners.Sequence(moves, corner, size)];
            if (seq.Count == 0)
            {
                continue;
            }

            if (seq[^1].Color == toMove)
            {
                seq.Add(new CornerMove(toMove.Opponent(), null)); // the opponent tenuki'd: the corner waits for toMove
            }

            sequences[corner] = seq;
            foreach (Point p in JosekiMatcher.Continuations(lines, seq, size))
            {
                if (Corners.Contains(corner, p, size))
                {
                    hints.TryAdd(p, new BoardJosekiHint(p, JosekiCategory.Unknown, IsLocal: true));
                }
            }
        }

        bool localKnowsLast = lastCorner is { } lc0 && sequences.ContainsKey(lc0) && hints.Keys.Any(p => Corners.Nearest(p, size) == lc0);
        Hints = [.. hints.Values];
        Description = null;
        SetText(sequences.Count == 0 ? null : localKnowsLast ? "Joseki.Hint.Library" : null, hints.Count);

        if (_explorer is null || size != 19 || sequences.Count == 0)
        {
            if (sequences.Count > 0 && hints.Count == 0)
            {
                SetText("Joseki.Hint.Unknown");
            }

            return;
        }

        IsSearching = true;
        try
        {
            Corner explorerCorner = await _explorer.CornerAsync(token);
            JosekiPosition? lastPosition = null;
            foreach ((Corner corner, IReadOnlyList<CornerMove> seq) in sequences)
            {
                foreach (Symmetry s in Corners.Mapping(corner, explorerCorner, size))
                {
                    Point?[] mapped = [.. seq.Select(m => m.Point is { } p ? s.Apply(p, size) : (Point?)null)];
                    JosekiPosition? position = await _explorer.FollowAsync(mapped, token);
                    token.ThrowIfCancellationRequested();
                    if (position is null)
                    {
                        continue;
                    }

                    if (corner == lastCorner && (lastPosition is null || Rank(position.Category) < Rank(lastPosition.Category)))
                    {
                        lastPosition = position;
                    }

                    Symmetry back = s.Inverse;
                    foreach (JosekiNextMove next in position.NextMoves.Where(m => m.Point is not null))
                    {
                        Point p = back.Apply(next.Point!.Value, size);
                        var hint = new BoardJosekiHint(p, next.Category, IsLocal: false);
                        if (!hints.TryGetValue(p, out BoardJosekiHint? existing) || existing.IsLocal || Rank(next.Category) < Rank(existing.Category))
                        {
                            hints[p] = hint;
                        }
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            Hints = [.. hints.Values];
            int nearLast = lastCorner is { } lc ? hints.Keys.Count(p => Corners.Nearest(p, size) == lc) : 0;
            if (lastPosition is not null)
            {
                Description = lastPosition.Description;
                SetText("Joseki.Hint.Ogs", CategoryName(lastPosition.Category), nearLast);
            }
            else if (lastCorner is { } c && sequences.ContainsKey(c))
            {
                SetText(localKnowsLast ? "Joseki.Hint.Library" : "Joseki.Hint.Unknown", nearLast);
            }

            _reportedFailure = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer position took over.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or IOException)
        {
            if (!_reportedFailure)
            {
                _logger.LogWarning(ex, "Joseki Explorer unavailable");
                _reportedFailure = true;
            }

            SetText("Joseki.Hint.Offline", hints.Count);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }

    private void SetText(string? key, params object?[] args)
    {
        _textKey = key;
        _textArgs = args;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HasText));
    }

    private static int Rank(JosekiCategory c) => c switch
    {
        JosekiCategory.Ideal => 0,
        JosekiCategory.Good => 1,
        JosekiCategory.Trick => 2,
        JosekiCategory.Question => 3,
        JosekiCategory.Mistake => 4,
        _ => 5,
    };

    internal static string CategoryName(JosekiCategory c) => Tr.T("Joseki.Category." + c);
}
