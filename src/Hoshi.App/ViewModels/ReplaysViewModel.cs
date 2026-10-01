using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>A row of the replay library.</summary>
public sealed class ReplayRow(ReplayEntry entry)
{
    public ReplayEntry Entry { get; } = entry;

    public string Players => $"{Name(Entry.Black, "Common.Black")} — {Name(Entry.White, "Common.White")}";

    public string Details => string.Join(" · ", new[]
    {
        Entry.Played.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        string.Create(CultureInfo.InvariantCulture, $"{Entry.Size}×{Entry.Size}"),
        Tr.F("Replays.Moves", Entry.Moves),
        Entry.Result ?? Tr.T("Replays.NoResult"),
        Entry.Source == "ogs" ? (Entry.OgsGameId is { } id ? string.Create(CultureInfo.InvariantCulture, $"OGS #{id}") : "OGS") : Tr.T("Replays.Local"),
    });

    private static string Name(string name, string fallbackKey) => string.IsNullOrWhiteSpace(name) ? Tr.T(fallbackKey) : name;
}

/// <summary>The "Played games" window: every game kept by <see cref="ReplayRecorder"/>, to review with the AI.</summary>
public sealed partial class ReplaysViewModel : ViewModelBase
{
    private readonly IReplayStore _store;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(DeleteCommand))]
    private ReplayRow? _selected;

    public ReplaysViewModel(IReplayStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        store.Changed += (_, _) => Reload();
        Reload();
    }

    /// <summary>Raised to show a replay on the board.</summary>
    public event EventHandler<ReplayEntry>? OpenRequested;

    public ObservableCollection<ReplayRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

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

    private bool HasSelection() => Selected is not null;

    private void Reload()
    {
        Rows.Clear();
        foreach (ReplayEntry e in _store.List())
        {
            Rows.Add(new ReplayRow(e));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
