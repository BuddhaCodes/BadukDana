using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Joseki;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.ViewModels;

/// <summary>
/// The joseki trainer: shows a line due for review in a random corner and orientation, the player places every
/// stone, and the result moves the line between Leitner boxes (a perfect run waits longer, a mistake comes back
/// soon). Closed to live OGS games the player is in, like any other study aid.
/// </summary>
public sealed partial class JosekiTrainerViewModel : ViewModelBase
{
    private readonly IJosekiLibrary _library;
    private readonly GameViewModel? _game;
    private readonly ISoundService? _sounds;
    private readonly ISettingsService? _settings;
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly Func<DateTimeOffset> _now;
    private readonly Random _random;
    private JosekiDrill? _drill;
    private bool _revealed;
    private bool _recorded;
    private Point? _wrong;
    private Point? _shownAnswer;
    private Symmetry? _symmetryBefore;
    private string _statusKey = "Joseki.Start";
    private object?[] _statusArgs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Markers))]
    private Point? _hoverPoint;

    public JosekiTrainerViewModel(
        IJosekiLibrary library,
        GameViewModel? game = null,
        ISoundService? sounds = null,
        ISettingsService? settings = null,
        IFileDialogService? files = null,
        IDialogService? dialogs = null,
        Func<DateTimeOffset>? clock = null,
        Random? random = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _game = game;
        _sounds = sounds;
        _settings = settings;
        _files = files;
        _dialogs = dialogs;
        _now = clock ?? (() => DateTimeOffset.Now);
        _random = random ?? Random.Shared;
        _library.Changed += (_, _) => Refresh();
        if (_game is not null)
        {
            _game.PropertyChanged += OnGamePropertyChanged;
        }
    }

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

    /// <summary>A live OGS game of the player is in progress: the trainer stays closed.</summary>
    public bool IsBlocked => _game?.Online is { IsPlayer: true, IsFinished: false };

    public bool HasLines => _library.Lines.Count > 0;

    public bool IsEmpty => !HasLines;

    public string LineName => _drill is null ? Tr.T("Joseki.Title") : Localize(_drill.Line.Name);

    public string ProgressText => _drill is null
        ? string.Empty
        : Tr.F("Joseki.Progress", Math.Min(_drill.Position + 1, _drill.Line.Moves.Count), _drill.Line.Moves.Count, ColorName(_drill.ToMove));

    public string StatusText => IsBlocked ? Tr.T("Joseki.BlockedOnline") : Tr.F(_statusKey, _statusArgs);

    public bool IsStatusGood => _statusKey is "Joseki.Correct" or "Joseki.Done" or "Joseki.Mirror";

    public bool IsStatusBad => _statusKey is "Joseki.Wrong" or "Joseki.Revealed" or "Joseki.DoneWithMistakes";

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

    /// <summary>Starts a session (called when the window opens).</summary>
    public void Start()
    {
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
    private void Play(Point point)
    {
        if (IsBlocked || _drill is null)
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

    /// <summary>The next line due (or, when everything is learned for now, a random one for extra practice).</summary>
    [RelayCommand]
    private void Next()
    {
        IReadOnlyList<JosekiLine> lines = _library.Lines;
        if (lines.Count == 0)
        {
            _drill = null;
            SetStatus("Joseki.NoLines");
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
        if (_drill is not null)
        {
            Begin(_drill.Line, "Joseki.Start");
        }
    }

    /// <summary>Shows the expected move; the run then no longer counts as perfect.</summary>
    [RelayCommand]
    private void ShowAnswer()
    {
        if (_drill is { IsComplete: false } && !IsBlocked)
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
            if (_drill is null)
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

    /// <summary>Keeps the moves from the start to the current position of the main board as a new line.</summary>
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

    private void Refresh()
    {
        if (_drill is not null && _shownAnswer is not null && _drill.Expected != _shownAnswer)
        {
            _shownAnswer = null;
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>The answer marker (the drill's own hint after two misses, or the one asked for).</summary>
    public Point? Answer => _drill?.Hint ?? _shownAnswer;

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline) or null or "")
        {
            OnPropertyChanged(nameof(IsBlocked));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(GhostStone));
        }
    }

    partial void OnHoverPointChanged(Point? value) => OnPropertyChanged(nameof(GhostStone));

    private static string ColorName(Stone stone) => stone switch
    {
        Stone.Black => Tr.T("Common.Black"),
        Stone.White => Tr.T("Common.White"),
        _ => string.Empty,
    };

    /// <summary>Starter lines name their texts with localization keys.</summary>
    internal static string Localize(string text) => text.StartsWith("Joseki.Starter.", StringComparison.Ordinal) && Tr.Has(text) ? Tr.T(text) : text;
}
