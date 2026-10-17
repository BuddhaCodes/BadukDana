using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>A row of the replay library; <see cref="MatchMove"/> is where a searched pattern appears.</summary>
public sealed class ReplayRow(ReplayEntry entry, int? matchMove = null)
{
    public ReplayEntry Entry { get; } = entry;

    public int? MatchMove { get; } = matchMove;

    public string Players => $"{Name(Entry.Black, "Common.Black")} — {Name(Entry.White, "Common.White")}";

    public string Details => string.Join(" · ", new[]
    {
        Entry.Played.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        string.Create(CultureInfo.InvariantCulture, $"{Entry.Size}×{Entry.Size}"),
        Tr.F("Replays.Moves", Entry.Moves),
        Entry.Result ?? Tr.T("Replays.NoResult"),
        Entry.Source == "ogs" ? (Entry.OgsGameId is { } id ? string.Create(CultureInfo.InvariantCulture, $"OGS #{id}") : "OGS") : Tr.T("Replays.Local"),
    }.Concat(MatchMove is { } m ? [Tr.F("Replays.PatternAt", m)] : []));

    private static string Name(string name, string fallbackKey) => string.IsNullOrWhiteSpace(name) ? Tr.T(fallbackKey) : name;
}

/// <summary>A choice of the result filter.</summary>
public sealed record ResultChoice(string Name, ResultFilter Filter)
{
    public override string ToString() => Name;
}

/// <summary>
/// The "Played games" window: every game kept by <see cref="ReplayRecorder"/>, to review with the AI. Searchable by
/// player, dates and result at once, and by a pattern taken from the main board (Kombilo-style: any corner or side,
/// either colours), which reads each game in the background.
/// </summary>
public sealed partial class ReplaysViewModel : ViewModelBase
{
    private readonly IReplayStore _store;
    private CancellationTokenSource? _search;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(DeleteCommand))]
    private ReplayRow? _selected;

    [ObservableProperty]
    private string _playerFilter = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _fromDate;

    [ObservableProperty]
    private DateTimeOffset? _toDate;

    [ObservableProperty]
    private ResultChoice _selectedResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPattern), nameof(PatternText))]
    [NotifyCanExecuteChangedFor(nameof(ClearPatternCommand))]
    private BoardPattern? _pattern;

    [ObservableProperty]
    private bool _isSearching;

    public ReplaysViewModel(IReplayStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Results =
        [
            new(Tr.T("Replays.AnyResult"), ResultFilter.Any),
            new(Tr.T("Replays.BlackWins"), ResultFilter.BlackWins),
            new(Tr.T("Replays.WhiteWins"), ResultFilter.WhiteWins),
            new(Tr.T("Replays.NoResult"), ResultFilter.NoResult),
        ];
        _selectedResult = Results[0];
        store.Changed += (_, _) => Reload();
        Reload();
    }

    /// <summary>Raised to show a replay on the board.</summary>
    public event EventHandler<ReplayEntry>? OpenRequested;

    /// <summary>The pattern to search, taken from the main board (set by the main window); null when there is none.</summary>
    public Func<BoardPattern?>? PatternSource { get; set; }

    public IReadOnlyList<ResultChoice> Results { get; }

    public ObservableCollection<ReplayRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public bool HasPattern => Pattern is not null;

    /// <summary>The date pickers' values (they work with <see cref="DateTime"/>).</summary>
    public DateTime? FromDateValue
    {
        get => FromDate?.Date;
        set => FromDate = value is { } d ? new DateTimeOffset(d.Date) : null;
    }

    public DateTime? ToDateValue
    {
        get => ToDate?.Date;
        set => ToDate = value is { } d ? new DateTimeOffset(d.Date) : null;
    }

    /// <summary>Why the list is empty: no games yet, or none that match.</summary>
    public string EmptyText => IsSearching ? Tr.T("Replays.Searching") : Query.IsEmpty ? Tr.T("Replays.Empty") : Tr.T("Replays.NoMatch");

    public string PatternText => Pattern is { } p ? Tr.F("Replays.PatternActive", p.Width, p.Height, p.Stones) : string.Empty;

    public GameSearchQuery Query => new(PlayerFilter, FromDate, ToDate, SelectedResult.Filter, Pattern);

    /// <summary>The searches running in the background finish (for tests).</summary>
    public Task Searching { get; private set; } = Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Open()
    {
        if (Selected is { } row)
        {
            OpenRequested?.Invoke(this, row.Entry);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is { } row)
        {
            _store.Delete(row.Entry);
        }
    }

    /// <summary>Searches the pattern on the main board (its study area, else the corner of the last move).</summary>
    [RelayCommand]
    private void UsePattern() => Pattern = PatternSource?.Invoke();

    [RelayCommand(CanExecute = nameof(HasPattern))]
    private void ClearPattern() => Pattern = null;

    [RelayCommand]
    private void ClearFilters()
    {
        PlayerFilter = string.Empty;
        FromDate = null;
        ToDate = null;
        SelectedResult = Results[0];
        Pattern = null;
    }

    private bool HasSelection() => Selected is not null;

    partial void OnPlayerFilterChanged(string value) => Reload();

    partial void OnFromDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(FromDateValue));
        Reload();
    }

    partial void OnToDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(ToDateValue));
        Reload();
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(EmptyText));

    partial void OnSelectedResultChanged(ResultChoice value) => Reload();

    partial void OnPatternChanged(BoardPattern? value) => Reload();

    private void Reload()
    {
        _search?.Cancel();
        GameSearchQuery query = Query;
        var entries = _store.List().Where(e => GameSearch.MatchesInfo(e, query)).ToList();
        Rows.Clear();
        if (query.Pattern is not { } pattern)
        {
            foreach (ReplayEntry e in entries)
            {
                Rows.Add(new ReplayRow(e));
            }

            IsSearching = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyText));
            return;
        }

        // Pattern: read the games in the background; rows appear as they match.
        var cts = new CancellationTokenSource();
        _search = cts;
        IsSearching = true;
        OnPropertyChanged(nameof(IsEmpty));
        IReadOnlyList<BoardPattern> variants = pattern.Variants();
        Searching = SearchAsync(entries, variants, cts.Token);
    }

    private async Task SearchAsync(IReadOnlyList<ReplayEntry> entries, IReadOnlyList<BoardPattern> variants, CancellationToken ct)
    {
        try
        {
            foreach (ReplayEntry entry in entries)
            {
                int? move = await Task.Run(
                    () =>
                    {
                        try
                        {
                            return GameSearch.FindPattern(_store.Load(entry), variants, ct);
                        }
                        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
                        {
                            return null;
                        }
                    },
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (move is { } m)
                {
                    Rows.Add(new ReplayRow(entry, m));
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            IsSearching = false;
        }
    }
}
