using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Sync with the game ----------

    private string AuthorName => Author.Trim() is { Length: > 0 } a ? a : Tr.T("Study.Me");

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameViewModel.TreeVersion):
                Sync();
                break;
            case nameof(GameViewModel.GhostStone) or nameof(GameViewModel.HoverPoint):
                OnPropertyChanged(nameof(GhostStone));
                if (_shapeStart is not null)
                {
                    OnPropertyChanged(nameof(BoardDrawings));
                }

                break;
            case nameof(GameViewModel.ConfirmMoves):
                if (!ConfirmEdits && _pendingDrawings is not null)
                {
                    ConfirmEdit(); // switched off: what was waiting is kept
                }

                OnPropertyChanged(nameof(ConfirmEdits));
                break;
            case nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline):
                OnPropertyChanged(nameof(IsLiveOnline));
                StartQuizCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    /// <summary>Reads the current move's study, the timeline and, for a new game, whether to open the panel.</summary>
    private void Sync()
    {
        if (!ReferenceEquals(_tree, _game.Tree))
        {
            bool first = _tree is null;
            _tree = _game.Tree;
            _sequenceId = null;
            if (IsQuizActive)
            {
                EndQuiz();
            }

            // A game that comes with a study (a shared file, a replay, a reloaded online game) opens the panel.
            if (!first && StudyStore.All(_tree).Any())
            {
                IsActive = true;
            }
        }

        if (!ReferenceEquals(_node, _game.CurrentNode))
        {
            _node = _game.CurrentNode;
            _sequenceId = null;
            DropPending(); // like a waiting stone, unconfirmed drawings stay behind when the move changes
        }

        NodeStudy study = Current();
        Drawings = study.Drawings;
        RebuildPins(study);
        RebuildTimeline();
        OnPropertyChanged(nameof(MoveTitle));
        OnPropertyChanged(nameof(GhostStone));
        OnPropertyChanged(nameof(IsLiveOnline));
    }

    private void RebuildPins(NodeStudy? study = null)
    {
        study ??= StudyStore.Read(_game.CurrentNode);
        Pins = [.. study.Pins.Select(p => new StudyPinRow(this, p))];
        OnPropertyChanged(nameof(HasPins));
    }

    private void RebuildTimeline()
    {
        IReadOnlyList<GameNode> track = Track();
        int moves = 0;
        int current = 0;
        var marks = new List<TimelineMark>();
        foreach (GameNode n in track)
        {
            if (n.HasMove)
            {
                moves++;
            }

            if (n == _game.CurrentNode)
            {
                current = moves;
            }

            NodeStudy s = StudyStore.Read(n);
            if (s.IsEmpty || !Matches(s))
            {
                continue;
            }

            if (s.Pins.Count == 0)
            {
                marks.Add(new TimelineMark(n, moves, null, $"{DescribeNode(n)} · {Tr.T("Study.DrawingsOnly")}"));
                continue;
            }

            foreach (StudyPin p in s.Pins.Where(p => Filter.Category is null || p.Category == Filter.Category))
            {
                string text = p.Text.Length > 60 ? p.Text[..57] + "…" : p.Text;
                marks.Add(new TimelineMark(n, moves, p.Category, $"{DescribeNode(n)} · {StudyReport.CategoryName(p.Category)}{(text.Length > 0 ? ": " + text : string.Empty)}"));
            }
        }

        TimelineTotal = Math.Max(1, moves);
        TimelineCurrent = current;
        Timeline = marks;
        OnPropertyChanged(nameof(TimelineText));
    }

    private bool Matches(NodeStudy study) =>
        !study.IsEmpty && (Filter.Category is not { } c ? true : study.Pins.Any(p => p.Category == c));

    /// <summary>Root → current move → on along the first variations: the line the timeline shows.</summary>
    private IReadOnlyList<GameNode> Track()
    {
        var track = GameCursor.Path(_game.CurrentNode).ToList();
        for (GameNode n = _game.CurrentNode; n.Children.Count > 0;)
        {
            n = n.Children[0];
            track.Add(n);
        }

        return track;
    }

    private string DescribeNode(GameNode node)
    {
        int number = GameCursor.Path(node).Count(n => n.HasMove);
        if (node.GetMove(_game.Cursor.BoardSize) is not { } m)
        {
            return number == 0 ? Tr.T("Study.Start") : Tr.F("Study.AfterMove", number);
        }

        string colour = Tr.T(m.Color == Stone.Black ? "Common.Black" : "Common.White");
        string where = m.Point is { } p ? p.ToHuman(_game.Board.Height) : Tr.T("Game.PassNoun");
        return Tr.F("Study.MoveName", number, colour, where);
    }

    internal static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.GetCultureInfo(Tr.Language == Tr.Spanish ? "es-ES" : "en-GB"));
}
