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
    // ---------- Tap and confirm ----------

    /// <summary>Board drawings wait for ✓ before they are saved: on with "Tap twice to play" (phones by default).</summary>
    public bool ConfirmEdits => _game.ConfirmMoves;

    /// <summary>Drawings were made on this move and wait for ✓ or ✗.</summary>
    public bool HasPendingEdit => _pendingDrawings is not null;

    /// <summary>The drawings shown faded because they are not saved yet.</summary>
    public IReadOnlyCollection<string> PendingDrawingIds
    {
        get
        {
            var ids = new List<string>();
            if (_shapeStart is not null)
            {
                ids.Add(ShapeStartId); // the two-tap shape in the making
            }

            if (_pendingDrawings is not null)
            {
                var saved = StudyStore.Read(_game.CurrentNode).Drawings.ToHashSet();
                ids.AddRange(_pendingDrawings.Where(d => !saved.Contains(d)).Select(d => d.Id));
            }

            return ids;
        }
    }

    /// <summary>✓: the drawings made since the last confirmation are saved.</summary>
    [RelayCommand]
    private void ConfirmEdit()
    {
        if (_pendingDrawings is not { } drawings || _pendingNode != _game.CurrentNode)
        {
            DropPending();
            return;
        }

        DropPending();
        Update(s => s with { Drawings = drawings });
        _game.ShowStatus(Tr.T("Study.EditSaved"));
    }

    /// <summary>✗: the drawings made since the last confirmation go away.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        DropPending();
        _sequenceId = null;
        Sync();
    }

    private void DropPending()
    {
        bool had = _pendingDrawings is not null || _shapeStart is not null;
        _pendingDrawings = null;
        _pendingNode = null;
        _shapeStart = null;
        if (had)
        {
            OnPropertyChanged(nameof(HasPendingEdit));
            OnPropertyChanged(nameof(PendingDrawingIds));
            OnPropertyChanged(nameof(BoardDrawings));
        }
    }

    /// <summary>This move's drawings as the board shows them: waiting ones included.</summary>
    private NodeStudy Current()
    {
        NodeStudy saved = StudyStore.Read(_game.CurrentNode);
        return _pendingDrawings is { } pending && _pendingNode == _game.CurrentNode ? saved with { Drawings = pending } : saved;
    }

    /// <summary>A drawing made on the board: saved at once, or kept waiting for ✓ with tap and confirm.</summary>
    private void EditDrawings(Func<IReadOnlyList<StudyDrawing>, IReadOnlyList<StudyDrawing>> change)
    {
        if (!ConfirmEdits)
        {
            Update(s => s with { Drawings = change(s.Drawings) });
            return;
        }

        _pendingDrawings = change(Current().Drawings);
        _pendingNode = _game.CurrentNode;
        Drawings = _pendingDrawings;
        OnPropertyChanged(nameof(HasPendingEdit));
        OnPropertyChanged(nameof(PendingDrawingIds));
        OnPropertyChanged(nameof(GhostStone));
    }

    [RelayCommand]
    private void PreviousPin() => GoToPin(forward: false);

    [RelayCommand]
    private void NextPin() => GoToPin(forward: true);

    [RelayCommand]
    private void GoToMark(TimelineMark? mark)
    {
        if (mark is not null)
        {
            _game.GoToNodeCommand.Execute(mark.Node);
        }
    }

    private void GoToPin(bool forward)
    {
        IReadOnlyList<GameNode> track = Track();
        int here = track.ToList().IndexOf(_game.CurrentNode);
        IEnumerable<GameNode> candidates = forward ? track.Skip(here + 1) : track.Take(Math.Max(0, here)).Reverse();
        if (candidates.FirstOrDefault(n => Matches(StudyStore.Read(n))) is { } target)
        {
            _game.GoToNodeCommand.Execute(target);
        }
        else
        {
            _game.ShowStatus(Tr.T(forward ? "Study.NoNextPin" : "Study.NoPreviousPin"));
        }
    }

    // ---------- Board ----------

    /// <summary>A click on the board: the quiz's answer, a study tool, or the normal move.</summary>
    [RelayCommand(CanExecute = nameof(CanBoardClick))]
    private void BoardClick(Point point)
    {
        if (IsQuizActive)
        {
            AnswerQuiz(point);
            return;
        }

        if (IsActive && Tool != StudyTool.Note)
        {
            ApplyTool(point);
            return;
        }

        if (_game.PlayCommand.CanExecute(point))
        {
            _game.PlayCommand.Execute(point);
        }
    }

    private bool CanBoardClick(Point point) => IsQuizActive || (IsActive && Tool != StudyTool.Note) || _game.PlayCommand.CanExecute(point);

    /// <summary>A drag on the board: an arrow or an area.</summary>
    [RelayCommand]
    private void BoardDrag(BoardDrag? drag)
    {
        if (drag is null || !IsActive || IsQuizActive || !_game.Board.IsOnBoard(drag.From) || !_game.Board.IsOnBoard(drag.To))
        {
            return;
        }

        DrawingKind? kind = Tool switch
        {
            StudyTool.Arrow => DrawingKind.Arrow,
            StudyTool.Area => DrawingKind.Area,
            _ => null,
        };
        if (kind is { } k)
        {
            _shapeStart = null;
            Add(new StudyDrawing(StudyStore.NewId(), k, AuthorName, Color, [drag.From, drag.To]));
            OnPropertyChanged(nameof(PendingDrawingIds));
        }
    }

    private void ApplyTool(Point point)
    {
        if (!_game.Board.IsOnBoard(point))
        {
            return;
        }

        NodeStudy study = Current();
        switch (Tool)
        {
            case StudyTool.Mark:
                if (study.Drawings.FirstOrDefault(d => d.Kind == DrawingKind.Mark && d.Points[0] == point) is { } mark)
                {
                    Remove(mark.Id);
                }
                else
                {
                    Add(new StudyDrawing(StudyStore.NewId(), DrawingKind.Mark, AuthorName, Color, [point]));
                }

                break;
            case StudyTool.Label:
                if (study.Drawings.FirstOrDefault(d => d.Kind == DrawingKind.Label && d.Points[0] == point) is { } label)
                {
                    Remove(label.Id);
                }
                else
                {
                    Add(new StudyDrawing(StudyStore.NewId(), DrawingKind.Label, AuthorName, Color, [point], NextLetter(study)));
                }

                break;
            case StudyTool.Sequence:
                ExtendSequence(study, point);
                break;
            case StudyTool.Erase:
                string[] hit = [.. study.Drawings.Where(d => Touches(d, point)).Select(d => d.Id)];
                if (hit.Length > 0)
                {
                    EditDrawings(ds => [.. ds.Where(d => !hit.Contains(d.Id))]);
                }

                break;
            case StudyTool.Arrow or StudyTool.Area:
                // Drag, or tap the start and then the end (a finger does not hide the point that way).
                if (_shapeStart is not { } start)
                {
                    _shapeStart = point;
                    _game.ShowStatus(Tr.T(Tool == StudyTool.Arrow ? "Study.ArrowEnd" : "Study.AreaEnd"));
                }
                else if (start == point)
                {
                    _shapeStart = null;
                    _game.ClearStatus();
                }
                else
                {
                    _shapeStart = null;
                    _game.ClearStatus();
                    Add(new StudyDrawing(StudyStore.NewId(), Tool == StudyTool.Arrow ? DrawingKind.Arrow : DrawingKind.Area, AuthorName, Color, [start, point]));
                }

                OnPropertyChanged(nameof(BoardDrawings));
                OnPropertyChanged(nameof(ShapeStart));
                OnPropertyChanged(nameof(PendingDrawingIds));
                break;
        }
    }

    private void ExtendSequence(NodeStudy study, Point point)
    {
        StudyDrawing? current = _sequenceId is { } id ? study.Drawings.FirstOrDefault(d => d.Id == id) : null;
        if (current is not null && current.Points.Count > 0 && current.Points[^1] == point)
        {
            // Clicking the last stone takes it back (the whole line when it was the only one).
            if (current.Points.Count == 1)
            {
                Remove(current.Id);
                _sequenceId = null;
            }
            else
            {
                Replace(current with { Points = [.. current.Points.Take(current.Points.Count - 1)] });
            }

            return;
        }

        if (_game.Board[point] != Stone.Empty || current?.Points.Contains(point) == true)
        {
            return;
        }

        if (current is null)
        {
            Stone first = SequenceStart(study);
            var line = new StudyDrawing(StudyStore.NewId(), DrawingKind.Sequence, AuthorName, Color, [point], first == Stone.White ? "W" : "B");
            _sequenceId = line.Id;
            Add(line);
        }
        else
        {
            Replace(current with { Points = [.. current.Points, point] });
        }
    }

    private Stone NextSequenceColor()
    {
        NodeStudy study = Current();
        if (_sequenceId is { } id && study.Drawings.FirstOrDefault(d => d.Id == id) is { } line)
        {
            return line.Points.Count % 2 == 0 ? line.FirstColor : line.FirstColor.Opponent();
        }

        return SequenceStart(study);
    }

    /// <summary>
    /// The first colour of a new line: the side to move, except on a move pinned as a mistake, where the line shows
    /// what that player should have played instead (and the quiz takes it as the answer).
    /// </summary>
    private Stone SequenceStart(NodeStudy study) =>
        study.Pins.Any(p => p.Category == PinCategory.Mistake) && _game.CurrentNode.GetMove(_game.Cursor.BoardSize) is { } m
            ? m.Color
            : _game.Board.ToMove;

    private static bool Touches(StudyDrawing d, Point p) => d.Kind switch
    {
        DrawingKind.Area => d.AreaPoints().Contains(p),
        _ => d.Points.Contains(p),
    };

    private static string NextLetter(NodeStudy study)
    {
        var used = study.Drawings.Where(d => d.Kind == DrawingKind.Label).Select(d => d.Text).ToHashSet();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!used.Contains(c.ToString()))
            {
                return c.ToString();
            }
        }

        return "?";
    }

    private void Add(StudyDrawing drawing) => EditDrawings(ds => [.. ds, drawing]);

    private void Remove(string id) => EditDrawings(ds => [.. ds.Where(d => d.Id != id)]);

    private void Replace(StudyDrawing drawing) => EditDrawings(ds => [.. ds.Select(d => d.Id == drawing.Id ? drawing : d)]);

    /// <summary>Changes the study of the current move, saves it in the tree and refreshes everything.</summary>
    private void Update(Func<NodeStudy, NodeStudy> change)
    {
        GameNode node = _game.CurrentNode;
        StudyStore.Write(node, change(StudyStore.Read(node)));
        _game.StudyEdited();
        Sync();
    }
}
