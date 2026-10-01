using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Joseki;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Ogs.Joseki;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.ViewModels;

/// <summary>A choice in the trainer's family filter; a null family means every line.</summary>
public sealed record JosekiFamilyOption(JosekiFamily? Family)
{
    public string Name => Family is { } f ? Tr.T("Joseki.Family." + f) : Tr.T("Joseki.Family.All");

    public override string ToString() => Name;
}

/// <summary>A line in the trainer's list.</summary>
public sealed class JosekiLineRow(JosekiLine line, JosekiCard? card)
{
    public JosekiLine Line { get; } = line;

    public string Name => JosekiTrainerViewModel.Localize(Line.Name);

    public string Detail => string.Join(" · ",
        Tr.T("Joseki.Family." + JosekiFamilies.Of(Line)),
        Tr.F("Replays.Moves", Line.Moves.Count),
        card is null ? Tr.T("Joseki.NewLine") : Tr.F("Joseki.Box", card.Box, Leitner.MaxBox));
}

/// <summary>
/// The joseki trainer, shown on the main board in place of the game (which stays untouched underneath). It
/// offers the line due for review in a random corner and orientation, or any line picked from the list; the
/// player places every stone, and the result moves the line between Leitner boxes. New lines can be fetched
/// from the OGS Joseki Explorer (only ideal and good moves). Closed during the player's live OGS games.
/// </summary>
public sealed partial class JosekiTrainerViewModel : ViewModelBase
{
    private const int MaxOgsLineLength = 16;
    private readonly IJosekiLibrary _library;
    private readonly GameViewModel? _game;
    private readonly ISoundService? _sounds;
    private readonly ISettingsService? _settings;
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly JosekiExplorer? _explorer;
    private readonly Func<DateTimeOffset> _now;
    private readonly Random _random;
    private JosekiDrill? _drill;
    private bool _revealed;
    private bool _recorded;
    private bool _choosing;
    private Point? _wrong;
    private Point? _shownAnswer;
    private Symmetry? _symmetryBefore;
    private string _statusKey = "Joseki.Start";
    private object?[] _statusArgs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostStone))]
    private Point? _hoverPoint;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchFromOgsCommand))]
    private bool _isFetching;

    [ObservableProperty]
    private JosekiFamilyOption _selectedFamily;

    [ObservableProperty]
    private JosekiLineRow? _selectedLine;

    public JosekiTrainerViewModel(
        IJosekiLibrary library,
        GameViewModel? game = null,
        ISoundService? sounds = null,
        ISettingsService? settings = null,
        IFileDialogService? files = null,
        IDialogService? dialogs = null,
        JosekiExplorer? explorer = null,
        Func<DateTimeOffset>? clock = null,
        Random? random = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _game = game;
        _sounds = sounds;
        _settings = settings;
        _files = files;
        _dialogs = dialogs;
        _explorer = explorer;
        _now = clock ?? (() => DateTimeOffset.Now);
        _random = random ?? Random.Shared;
        Families = [new JosekiFamilyOption(null), .. JosekiFamilies.All.Select(f => new JosekiFamilyOption(f))];
        _selectedFamily = Families[0];
        _library.Changed += (_, _) => Refresh();
        if (_game is not null)
        {
            _game.PropertyChanged += OnGamePropertyChanged;
        }

        RebuildRows();
    }

    public IReadOnlyList<JosekiFamilyOption> Families { get; }

    public ObservableCollection<JosekiLineRow> Rows { get; } = [];

    public JosekiDrill? Drill => _drill;

    public BoardState Board => _drill?.Board ?? BoardState.Create(19);

    public Point? LastMove => _drill?.LastMove;

    public Stone GhostStone => IsBlocked || _drill is null || HoverPoint is not { } p || !Board.IsLegal(_drill.ToMove, p)
        ? Stone.Empty
        : _drill.ToMove;

    public IReadOnlyList<Markup> Markers
    {
        get
        {
            var list = new List<Markup>();
            if (Answer is { } hint)
            {
                list.Add(new Markup(hint, MarkupKind.Circle));
            }

            if (_wrong is { } wrong)
            {
                list.Add(new Markup(wrong, MarkupKind.Cross));
            }

            return list;
        }
    }

    /// <summary>The answer marker (the drill's own hint after two misses, or the one asked for).</summary>
    public Point? Answer => _drill?.Hint ?? _shownAnswer;

    /// <summary>A live OGS game of the player is in progress: the trainer stays closed.</summary>
    public bool IsBlocked => _game?.Online is { IsPlayer: true, IsFinished: false };

    public bool HasExplorer => _explorer is not null;

    public bool IsEmpty => _library.Lines.Count == 0;

    public string LineName => _drill is null ? Tr.T("Joseki.Title") : Localize(_drill.Line.Name);

    public string ProgressText => _drill is null
        ? string.Empty
        : Tr.F("Joseki.Progress", Math.Min(_drill.Position + 1, _drill.Line.Moves.Count), _drill.Line.Moves.Count, ColorName(_drill.ToMove));

    public string StatusText => IsBlocked ? Tr.T("Joseki.BlockedOnline") : Tr.F(_statusKey, _statusArgs);

    public bool IsStatusGood => _statusKey is "Joseki.Correct" or "Joseki.Done" or "Joseki.Mirror" or "Joseki.Fetched";

    public bool IsStatusBad => _statusKey is "Joseki.Wrong" or "Joseki.Revealed" or "Joseki.DoneWithMistakes" or "Joseki.FetchFailed";

    /// <summary>The note of the line, shown once it is complete.</summary>
    public string? Note => _drill is { IsComplete: true, Line.Comment: { } c } ? Localize(c) : null;

    public bool HasNote => Note is not null;

    public string LibraryText
    {
        get
        {
            IReadOnlyDictionary<string, JosekiCard> cards = _library.Cards;
            IReadOnlyList<JosekiLine> lines = _library.Lines;
            int due = Leitner.DueCount(lines.Select(l => l.Id), cards, _now());
            int mastered = lines.Count(l => cards.TryGetValue(l.Id, out JosekiCard? c) && c.Box >= 4);
            return Tr.F("Joseki.LibraryStats", lines.Count, due, mastered);
        }
    }

    public string? BoxText => _drill is not null && _library.Cards.TryGetValue(_drill.Line.Id, out JosekiCard? card)
        ? Tr.F("Joseki.Box", card.Box, Leitner.MaxBox)
        : _drill is null ? null : Tr.T("Joseki.NewLine");

    /// <summary>Ctrl+J: shows the trainer on the main board, or goes back to the game.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsActive)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        if (IsBlocked)
        {
            SetStatus("Joseki.BlockedOnline");
            Refresh();
            return;
        }

        IsActive = true;
        if (_drill is null || _drill.IsComplete)
        {
            Next();
        }
        else
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void Close() => IsActive = false;

    [RelayCommand]
    private void Play(Point point)
    {
        if (IsBlocked || _drill is null || !IsActive)
        {
            return;
        }

        _wrong = null;
        DrillAnswer answer = _drill.Attempt(point);
        switch (answer)
        {
            case DrillAnswer.Correct:
                SetStatus(_drill.Symmetry != _symmetryBefore ? "Joseki.Mirror" : "Joseki.Correct");
                PlaySound(SoundEffect.Stone, 0.8);
                break;
            case DrillAnswer.Completed:
                bool perfect = _drill.Mistakes == 0 && !_revealed;
                RecordResult(perfect);
                SetStatus(perfect ? "Joseki.Done" : "Joseki.DoneWithMistakes");
                PlaySound(SoundEffect.Stone, 0.8);
                if (perfect)
                {
                    PlaySound(SoundEffect.ImpactSmall, 0.6);
                }

                break;
            case DrillAnswer.AlsoJoseki:
                SetStatus("Joseki.AlsoJoseki");
                break;
            case DrillAnswer.Wrong:
                _wrong = point;
                SetStatus(_drill.Hint is null ? "Joseki.Wrong" : "Joseki.Revealed");
                if (_drill.Hint is not null)
                {
                    _revealed = true;
                }

                break;
            default:
                return;
        }

        _symmetryBefore = _drill.Symmetry;
        Refresh();
    }

    /// <summary>The next line due in the chosen family (or, when everything is learned for now, a random one).</summary>
    [RelayCommand]
    private void Next()
    {
        if (!IsActive)
        {
            return;
        }

        List<JosekiLine> lines = [.. FilteredLines()];
        if (lines.Count == 0)
        {
            _drill = null;
            SetStatus(_library.Lines.Count == 0 ? "Joseki.NoLines" : "Joseki.NoLinesInFamily");
            Refresh();
            return;
        }

        string? id = Leitner.Next(lines.Select(l => l.Id), _library.Cards, _now(), _random);
        JosekiLine line = lines.FirstOrDefault(l => l.Id == id) ?? lines[_random.Next(lines.Count)];
        Begin(line, id is null ? "Joseki.ExtraPractice" : "Joseki.Start");
    }

    /// <summary>Same line again, in a new corner.</summary>
    [RelayCommand]
    private void Restart()
    {
        if (_drill is not null && IsActive)
        {
            Begin(_drill.Line, "Joseki.Start");
        }
    }

    /// <summary>Shows the expected move; the run then no longer counts as perfect.</summary>
    [RelayCommand]
    private void ShowAnswer()
    {
        if (_drill is { IsComplete: false } && !IsBlocked && IsActive)
        {
            _revealed = true;
            _shownAnswer = _drill.Expected;
            SetStatus("Joseki.Revealed");
            Refresh();
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (_files is null || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        try
        {
            int added = _library.Import(path);
            SetStatus("Joseki.Imported", added);
            if (_drill is null && IsActive)
            {
                Next();
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Joseki.ImportFailed"), ex.Message);
            }
        }

        Refresh();
    }

    /// <summary>Keeps the moves from the start to the current position of the game as a new line.</summary>
    [RelayCommand]
    private void AddFromBoard()
    {
        if (_game is null || IsBlocked)
        {
            return;
        }

        GameCursor cursor = _game.Cursor;
        List<JosekiMove> moves = [.. GameCursor.Path(cursor.Current)
            .Select(n => n.GetMove(cursor.BoardSize))
            .Where(m => m is { Point: not null })
            .Select(m => new JosekiMove(m!.Value.Color, m.Value.Point!.Value))];
        string name = Tr.F("Joseki.MyLineName", _library.Lines.Count + 1);
        JosekiLine? added = moves.Count < 2 ? null : _library.AddLine(moves, cursor.BoardSize, name);
        SetStatus(added is not null ? "Joseki.Added" : moves.Count < 2 ? "Joseki.AddTooShort" : "Joseki.AddKnown");
        Refresh();
    }

    private bool CanFetch() => _explorer is not null && !IsFetching;

    /// <summary>
    /// Builds a new line from the OGS Joseki Explorer: from the root, a random walk through ideal (more often)
    /// and good moves of the chosen family, until the explorer suggests tenuki or has nothing more; kept in
    /// <see cref="JosekiLibrary.OgsLinesFile"/> and started at once.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchFromOgs()
    {
        if (_explorer is null || IsBlocked)
        {
            return;
        }

        IsFetching = true;
        SetStatus("Joseki.Fetching");
        Refresh();
        try
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                (List<JosekiMove> moves, string? note) = await WalkAsync(CancellationToken.None);
                if (moves.Count < 4)
                {
                    continue;
                }

                string family = Tr.T("Joseki.Family." + JosekiFamilies.Of(moves[0].Point, 19));
                JosekiLine? line = _library.AddLine(moves, 19, Tr.F("Joseki.OgsLineName", family, moves.Count), note, JosekiLibrary.OgsLinesFile);
                if (line is not null)
                {
                    IsActive = true;
                    Begin(line, "Joseki.Fetched");
                    return;
                }
            }

            SetStatus("Joseki.FetchNothingNew");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or IOException)
        {
            SetStatus("Joseki.FetchFailed");
        }
        finally
        {
            IsFetching = false;
            Refresh();
        }
    }

    private async Task<(List<JosekiMove> Moves, string? Note)> WalkAsync(CancellationToken token)
    {
        var moves = new List<JosekiMove>();
        JosekiPosition position = await _explorer!.Source.GetPositionAsync("root", token);
        string? note = null;
        while (moves.Count < MaxOgsLineLength)
        {
            List<JosekiNextMove> options = [.. position.NextMoves.Where(m => m.IsRecommended)];
            if (moves.Count == 0)
            {
                options = [.. options.Where(m => m.Point is { } p && (SelectedFamily.Family is null || JosekiFamilies.Of(p, 19) == SelectedFamily.Family))];
            }
            else
            {
                options = [.. options.Where(m => m.Point is null || Board19.IsLegal(Color(moves.Count), m.Point.Value) && !moves.Any(x => x.Point == m.Point))];
            }

            if (options.Count == 0)
            {
                break;
            }

            JosekiNextMove pick = Weighted(options);
            if (pick.Point is not { } p)
            {
                break; // tenuki: the joseki is over
            }

            moves.Add(new JosekiMove(Color(moves.Count), p));
            position = await _explorer.Source.GetPositionAsync(pick.NodeId, token);
            note = position.Description ?? note;
        }

        return (moves, note);

        static Stone Color(int index) => index % 2 == 0 ? Stone.Black : Stone.White;
    }

    // Good enough for the walk's legality check: joseki stones are never captured in the first few moves.
    private static readonly BoardState Board19 = BoardState.Create(19);

    private JosekiNextMove Weighted(List<JosekiNextMove> options)
    {
        int total = options.Sum(Weight);
        int roll = _random.Next(total);
        foreach (JosekiNextMove o in options)
        {
            roll -= Weight(o);
            if (roll < 0)
            {
                return o;
            }
        }

        return options[^1];

        static int Weight(JosekiNextMove m) => m.Category == JosekiCategory.Ideal ? 3 : 1;
    }

    partial void OnSelectedFamilyChanged(JosekiFamilyOption value)
    {
        RebuildRows();
        if (IsActive && _drill is not null && value.Family is { } f && JosekiFamilies.Of(_drill.Line) != f)
        {
            Next();
        }
    }

    partial void OnSelectedLineChanged(JosekiLineRow? value)
    {
        if (value is not null && !_choosing && IsActive)
        {
            Begin(value.Line, "Joseki.Start");
        }
    }

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    private IEnumerable<JosekiLine> FilteredLines() =>
        _library.Lines.Where(l => SelectedFamily.Family is not { } f || JosekiFamilies.Of(l) == f);

    private void Begin(JosekiLine line, string statusKey)
    {
        _drill = new JosekiDrill(line, Symmetry.All[_random.Next(Symmetry.All.Count)], _library.Lines);
        _symmetryBefore = _drill.Symmetry;
        _revealed = false;
        _recorded = false;
        _wrong = null;
        _shownAnswer = null;
        SetStatus(statusKey);
        Refresh();
    }

    private void RecordResult(bool perfect)
    {
        if (_drill is null || _recorded)
        {
            return;
        }

        _recorded = true;
        JosekiCard card = _library.Cards.TryGetValue(_drill.Line.Id, out JosekiCard? c) ? c : JosekiCard.New(_drill.Line.Id);
        _library.Record(card.Review(perfect, _now()));
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
    }

    private void PlaySound(SoundEffect effect, double gain)
    {
        AppSettings s = _settings?.Current ?? new AppSettings();
        if (_sounds is not null && s.StoneSounds)
        {
            _sounds.Play(effect, s.SoundVolume / 100.0 * gain);
        }
    }

    private void RebuildRows()
    {
        _choosing = true;
        try
        {
            IReadOnlyDictionary<string, JosekiCard> cards = _library.Cards;
            string? selected = _drill?.Line.Id ?? SelectedLine?.Line.Id;
            Rows.Clear();
            foreach (JosekiLine line in FilteredLines())
            {
                Rows.Add(new JosekiLineRow(line, cards.TryGetValue(line.Id, out JosekiCard? c) ? c : null));
            }

            SelectedLine = Rows.FirstOrDefault(r => r.Line.Id == selected);
        }
        finally
        {
            _choosing = false;
        }
    }

    private void Refresh()
    {
        if (_drill is not null && _shownAnswer is not null && _drill.Expected != _shownAnswer)
        {
            _shownAnswer = null;
        }

        RebuildRows();
        OnPropertyChanged(string.Empty);
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline))
        {
            if (IsBlocked)
            {
                IsActive = false;
            }

            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(GhostStone));
        }
    }

    private static string ColorName(Stone stone) => stone switch
    {
        Stone.Black => Tr.T("Common.Black"),
        Stone.White => Tr.T("Common.White"),
        _ => string.Empty,
    };

    /// <summary>Starter lines name their texts with localization keys.</summary>
    internal static string Localize(string text) => text.StartsWith("Joseki.Starter.", StringComparison.Ordinal) && Tr.Has(text) ? Tr.T(text) : text;
}
