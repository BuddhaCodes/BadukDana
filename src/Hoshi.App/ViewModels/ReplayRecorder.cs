using System.ComponentModel;
using Hoshi.App.Services;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Keeps every game played in Hoshi in the replay library: a local game when it is replaced (New, Open, a replay,
/// an online game) or when the app closes; an OGS game when it ends (and, partially, if it is left). A replay
/// opened from the library is only saved again if it was changed.
/// </summary>
public sealed class ReplayRecorder
{
    private readonly GameViewModel _game;
    private readonly IReplayStore _store;
    private string _kind = "local";
    private string _id = ReplayStore.NewLocalId();
    private long? _ogsGameId;
    private OnlineGameViewModel? _watched;

    public ReplayRecorder(GameViewModel game, IReplayStore store)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        game.TreeReplacing += (_, old) => OnTreeReplacing(old);
        game.PropertyChanged += OnGamePropertyChanged;
    }

    /// <summary>The game on the board came from the library.</summary>
    public void MarkReplay(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _kind = "replay:" + entry.Source;
        _id = entry.Id;
        _ogsGameId = entry.OgsGameId;
    }

    /// <summary>Saves the game on the board now (app closing).</summary>
    public void SaveCurrent() => Save(_game.Tree, _game.IsDirty);

    private void OnTreeReplacing(GameTree old)
    {
        Save(old, _game.IsDirty);
        if (_game.Online is { } online)
        {
            _kind = "ogs";
            _id = "ogs-" + online.GameId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ogsGameId = online.GameId;
        }
        else
        {
            _kind = "local";
            _id = ReplayStore.NewLocalId();
            _ogsGameId = null;
        }
    }

    private void Save(GameTree tree, bool changed)
    {
        if (_kind.StartsWith("replay:", StringComparison.Ordinal))
        {
            if (changed)
            {
                _store.Save(tree, _id, _kind["replay:".Length..], _ogsGameId);
            }

            return;
        }

        _store.Save(tree, _id, _kind, _ogsGameId);
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameViewModel.Online))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnOnlinePropertyChanged;
        }

        _watched = _game.Online;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnOnlinePropertyChanged;
        }
    }

    private void OnOnlinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnlineGameViewModel.IsFinished) && sender is OnlineGameViewModel { IsFinished: true } online
            && _kind == "ogs")
        {
            _store.Save(_game.Tree, _id, "ogs", online.GameId);
        }
    }
}
