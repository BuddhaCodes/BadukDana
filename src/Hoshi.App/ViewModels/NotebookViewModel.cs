using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

/// <summary>One note of the notebook: a pin of a game of the library, at its move.</summary>
public sealed record NotebookRow(ReplayEntry Entry, int Move, StudyPin Pin, string Players)
{
    public string CategoryName => StudyReport.CategoryName(Pin.Category);

    public string Colour => StudyReport.Colour(Pin.Category);

    /// <summary>"Ana — Ben · move 34 · 2 Nov".</summary>
    public string Where => string.Join(" · ", Players, Tr.F("Notebook.Move", Move), Entry.Played.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));

    /// <summary>The note, or what the category says when it is empty; replies are counted.</summary>
    public string Text => (Pin.Text.Length > 0 ? Pin.Text : CategoryName)
        + (Pin.Replies.Count > 0 ? "  " + Tr.F("Notebook.Replies", Pin.Replies.Count) : string.Empty);

    /// <summary>Searchable text: note, replies, authors and players.</summary>
    internal bool Matches(string query) =>
        query.Length == 0
        || Contains(Pin.Text, query) || Contains(Pin.Author, query) || Contains(Players, query)
        || Pin.Replies.Any(r => Contains(r.Text, query) || Contains(r.Author, query));

    private static bool Contains(string text, string query) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Raised to show a note: its game, at its move.</summary>
public sealed record NotebookOpen(ReplayEntry Entry, int Move);

/// <summary>
/// The notebook: every note pinned in the games of the library in one list, newest game first, to find "all my
/// mistakes in the opening", "every note that mentions the ladder", and jump to the game at that move. Filtered by
/// category and by text (notes, replies, authors and players). Reads the games in the background.
/// </summary>
public sealed partial class NotebookViewModel : ViewModelBase
{
    private readonly IReplayStore _store;
    private List<NotebookRow> _all = [];
    private int _loads;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private NotebookRow? _selected;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private StudyCategoryOption _category;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isLoading;

    public NotebookViewModel(IReplayStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Categories = [new StudyCategoryOption(null), .. Enum.GetValues<PinCategory>().Select(c => new StudyCategoryOption(c))];
        _category = Categories[0];
        store.Changed += (_, _) =>
        {
            if (_loads > 0)
            {
                _ = ReloadAsync(); // only once shown: reading every game costs a moment
            }
        };
    }

    public event EventHandler<NotebookOpen>? OpenRequested;

    public IReadOnlyList<StudyCategoryOption> Categories { get; }

    public ObservableCollection<NotebookRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>"12 notes in 5 games".</summary>
    public string CountText => Tr.F("Notebook.Count", Rows.Count, Rows.Select(r => r.Entry.Id).Distinct().Count());

    public string EmptyText => IsLoading ? Tr.T("Notebook.Loading")
        : _all.Count == 0 ? Tr.T("Notebook.Empty")
        : Tr.T("Notebook.NoMatch");

    /// <summary>The reading running in the background (for tests).</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The first time the notebook is shown.</summary>
    public Task EnsureLoadedAsync() => _loads == 0 ? ReloadAsync() : Loading;

    /// <summary>Reads every game's notes again (the library changed, or the notebook is shown).</summary>
    public Task ReloadAsync()
    {
        Loading = Load(++_loads);
        return Loading;
    }

    private async Task Load(int load)
    {
        IsLoading = true;
        List<NotebookRow> rows;
        try
        {
            rows = await Task.Run(() => Read(_store));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows = [];
        }

        if (load != _loads)
        {
            return; // a newer reading is on its way
        }

        _all = rows;
        IsLoading = false;
        Filter();
    }

    internal static List<NotebookRow> Read(IReplayStore store)
    {
        var rows = new List<NotebookRow>();
        foreach (ReplayEntry entry in store.List())
        {
            GameTree tree;
            try
            {
                tree = store.Load(entry);
            }
            catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
            {
                continue;
            }

            string players = $"{Name(entry.Black, "Common.Black")} — {Name(entry.White, "Common.White")}";
            foreach ((GameNode node, NodeStudy study) in StudyStore.All(tree))
            {
                int move = MoveNumber(node);
                rows.AddRange(study.Pins.Select(pin => new NotebookRow(entry, move, pin, players)));
            }
        }

        return rows;
    }

    partial void OnSearchChanged(string value) => Filter();

    partial void OnCategoryChanged(StudyCategoryOption value) => Filter();

    private void Filter()
    {
        string query = Search.Trim();
        Rows.Clear();
        foreach (NotebookRow row in _all
            .Where(r => Category.Category is not { } c || r.Pin.Category == c)
            .Where(r => r.Matches(query))
            .OrderByDescending(r => r.Entry.Played)
            .ThenBy(r => r.Move))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Open()
    {
        if (Selected is { } row)
        {
            OpenRequested?.Invoke(this, new NotebookOpen(row.Entry, row.Move));
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Search = string.Empty;
        Category = Categories[0];
    }

    private static int MoveNumber(GameNode node)
    {
        int n = 0;
        for (GameNode? at = node; at is not null; at = at.Parent)
        {
            n += at.HasMove ? 1 : 0;
        }

        return n;
    }

    private static string Name(string name, string fallbackKey) => string.IsNullOrWhiteSpace(name) ? Tr.T(fallbackKey) : name;
}
