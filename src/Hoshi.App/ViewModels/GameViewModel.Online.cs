using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class GameViewModel
{
    // ---------- Online games (driven by OnlineGameViewModel) ----------

    /// <summary>Shows an online game: its tree replaces the current one and clicks go to OGS.</summary>
    public void LoadOnline(GameTree tree, OnlineGameViewModel online)
    {
        ArgumentNullException.ThrowIfNull(online);

        // The same game again (reconnect, gamedata): keep the study notes made on it so far.
        GameTree? previous = ReferenceEquals(Online, online) && !ReferenceEquals(Tree, tree) ? Tree : null;
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

        if (previous is not null)
        {
            Hoshi.Sgf.Study.StudyStore.Merge(tree, previous, createMissingMoves: false);
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
}
