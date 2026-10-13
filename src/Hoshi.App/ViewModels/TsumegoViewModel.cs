using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Tsumego;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Problems;

namespace Hoshi.App.ViewModels;

/// <summary>A problem in the list, with how it is going.</summary>
public sealed record TsumegoRow(TsumegoProblem Problem, string Name, string Source, string State);

/// <summary>
/// Problems (tsumego): play the answer on the board, the opponent's replies come from the problem, a wrong move ends
/// the try (with the refutation when the collection has one). Right answers move a problem up its Leitner box, so
/// "Next" brings back the ones that are due. Closed during the player's live OGS games, like the joseki trainer.
/// </summary>
public sealed partial class TsumegoViewModel : ViewModelBase
{
    private readonly TsumegoLibrary _library;
    private readonly IFileDialogService? _files;
    private readonly GameViewModel? _game;
    private readonly TimeProvider _time;
    private readonly List<TsumegoSource> _sources = [];
    private TsumegoAttempt? _attempt;
    private bool _showAnswer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Instructions), nameof(Board), nameof(LastMove), nameof(Markers), nameof(GhostStone), nameof(StatusText), nameof(IsSolved), nameof(IsWrong), nameof(ProgressText))]
    private TsumegoProblem? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostStone))]
    private Point? _hoverPoint;

    [ObservableProperty]
    private string _status = string.Empty;

    public TsumegoViewModel(TsumegoLibrary? library = null, IFileDialogService? files = null, GameViewModel? game = null, TimeProvider? time = null, TsumegoSource? builtIn = null)
    {
        _library = library ?? new TsumegoLibrary();
        _files = files;
        _game = game;
        _time = time ?? TimeProvider.System;
        AddSource(builtIn ?? TsumegoLibrary.BuiltIn());
        Current = _library.Next(AllProblems, null, _time.GetUtcNow());
    }

    public ObservableCollection<TsumegoRow> Rows { get; } = [];

    public IReadOnlyList<TsumegoProblem> AllProblems => [.. _sources.SelectMany(s => s.Problems)];

    public string Name => Current is { } p ? Tr.T(p.Name) : Tr.T("Tsumego.Title");

    public string Instructions => Current?.Instructions is { } i ? Tr.T(i) : Tr.F("Tsumego.ToPlay", Tr.T(Current?.ToPlay == Stone.White ? "Common.White" : "Common.Black"));

    public BoardState Board => _attempt?.Board ?? Current?.Start ?? BoardState.Create(9);

    public Point? LastMove => _attempt?.LastMove;

    public bool IsSolved => _attempt?.Result == TsumegoOutcome.Solved;

    public bool IsWrong => _attempt?.Result == TsumegoOutcome.Wrong;

    /// <summary>Problems are closed while the player has a live OGS game (no outside help there).</summary>
    public bool IsBlocked => _game?.Online is { IsPlayer: true, IsFinished: false };

    public string StatusText => IsBlocked ? Tr.T("Tsumego.Blocked") : Status;

    public string ProgressText
    {
        get
        {
            IReadOnlyList<TsumegoProblem> all = AllProblems;
            int solved = all.Count(p => _library.Cards.TryGetValue(p.Id, out TsumegoCard? c) && c.Box > 0);
            return Tr.F("Tsumego.Progress", solved, all.Count);
        }
    }

    /// <summary>The answer's next move, circled when asked for.</summary>
    public IReadOnlyList<Markup> Markers => _showAnswer && _attempt?.Hint is { } hint ? [new Markup(hint, MarkupKind.Circle)] : [];

    public Stone GhostStone => !IsBlocked && Current is { } p && _attempt is { IsFinished: false } && HoverPoint is { } q && Board.IsLegal(p.ToPlay, q)
        ? p.ToPlay
        : Stone.Empty;

    [RelayCommand]
    private void Play(Point point)
    {
        if (IsBlocked || Current is null || _attempt is null)
        {
            return;
        }

        if (_attempt.IsFinished)
        {
            return;
        }

        TsumegoOutcome outcome = _attempt.Play(point);
        _showAnswer = false;
        switch (outcome)
        {
            case TsumegoOutcome.Solved:
                _library.Record(Current, solved: !_usedAnswer, _time.GetUtcNow());
                Status = Explain(_usedAnswer ? "Tsumego.SolvedWithHelp" : "Tsumego.Solved");
                RefreshRows();
                break;
            case TsumegoOutcome.Wrong:
                _library.Record(Current, solved: false, _time.GetUtcNow());
                Status = Explain("Tsumego.Wrong");
                RefreshRows();
                break;
            case TsumegoOutcome.Continue:
                Status = Tr.T("Tsumego.KeepGoing");
                break;
            default:
                Status = Tr.T("Tsumego.Illegal");
                break;
        }

        Changed();
    }

    private bool _usedAnswer;

    [RelayCommand]
    private void Retry()
    {
        if (Current is { } p)
        {
            Start(p);
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (_library.Next(AllProblems, Current, _time.GetUtcNow()) is { } next)
        {
            Current = next;
        }
    }

    [RelayCommand]
    private void Previous()
    {
        IReadOnlyList<TsumegoProblem> all = AllProblems;
        if (Current is { } p && all.Count > 0)
        {
            int i = all.ToList().IndexOf(p);
            Current = all[(i - 1 + all.Count) % all.Count];
        }
    }

    /// <summary>Circles the next move of the answer (the problem then counts as not solved alone).</summary>
    [RelayCommand]
    private void ShowAnswer()
    {
        if (_attempt is { IsFinished: false, Hint: not null })
        {
            _showAnswer = true;
            _usedAnswer = true;
            Status = Tr.T("Tsumego.AnswerShown");
            Changed();
        }
    }

    [RelayCommand]
    private void Select(TsumegoRow? row)
    {
        if (row is not null)
        {
            Current = row.Problem;
        }
    }

    /// <summary>Opens an SGF problem collection (each game is a problem; "RIGHT"/"WRONG" in the comments).</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (_files is null || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        try
        {
            TsumegoSource source = TsumegoLibrary.Parse(Path.GetFileNameWithoutExtension(path), await File.ReadAllTextAsync(path));
            if (source.Problems.Count == 0)
            {
                Status = Tr.T("Tsumego.NoProblems");
                return;
            }

            AddSource(source);
            Current = source.Problems[0];
            Status = Tr.F("Tsumego.Imported", source.Problems.Count, source.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Adds a collection given as SGF text (tests, pasted problems).</summary>
    public void AddSource(TsumegoSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        RefreshRows();
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnCurrentChanged(TsumegoProblem? value)
    {
        if (value is not null)
        {
            Start(value);
        }
    }

    private void Start(TsumegoProblem problem)
    {
        _attempt = new TsumegoAttempt(problem);
        _showAnswer = false;
        _usedAnswer = false;
        Status = Tr.F("Tsumego.ToPlay", Tr.T(problem.ToPlay == Stone.White ? "Common.White" : "Common.Black"));
        Changed();
    }

    private string Explain(string key) =>
        _attempt?.Comment is { Length: > 0 } c && !c.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) && !c.Equals("WRONG", StringComparison.OrdinalIgnoreCase)
            ? $"{Tr.T(key)} {c}"
            : Tr.T(key);

    private void Changed()
    {
        OnPropertyChanged(nameof(Board));
        OnPropertyChanged(nameof(LastMove));
        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(GhostStone));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsSolved));
        OnPropertyChanged(nameof(IsWrong));
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusText));

    private void RefreshRows()
    {
        Rows.Clear();
        DateTimeOffset now = _time.GetUtcNow();
        foreach (TsumegoSource s in _sources)
        {
            foreach (TsumegoProblem p in s.Problems)
            {
                string state = !_library.Cards.TryGetValue(p.Id, out TsumegoCard? c) ? Tr.T("Tsumego.New")
                    : c.Box == 0 ? Tr.T("Tsumego.ToRedo")
                    : c.Due <= now ? Tr.T("Tsumego.Due")
                    : Tr.F("Tsumego.Box", c.Box);
                Rows.Add(new TsumegoRow(p, Tr.T(p.Name), s.Name, state));
            }
        }
    }
}
