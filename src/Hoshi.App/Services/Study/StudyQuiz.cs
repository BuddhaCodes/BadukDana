using Hoshi.Core;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Services.Study;

/// <summary>
/// One "What would you play?" question: the position before a pinned move. When the answer is known (a good
/// move that was played, or the first stone of a "what if" sequence drawn for a mistake) the guess is scored;
/// otherwise the player compares with the pin and grades themselves.
/// </summary>
public sealed record QuizQuestion(GameNode Position, GameNode Pinned, StudyPin Pin, Stone ToPlay, Point? Played, Point? Answer)
{
    public bool IsScored => Answer is not null;
}

public static class StudyQuiz
{
    /// <summary>Questions from the pins of the game, in move order (only pins on moves, one per move).</summary>
    public static IReadOnlyList<QuizQuestion> Build(GameTree tree, ISet<PinCategory>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        int size = Math.Max(tree.Info.Width, tree.Info.Height);
        var cursor = new GameCursor(tree);
        var questions = new List<QuizQuestion>();
        foreach ((GameNode node, NodeStudy study) in StudyStore.All(tree))
        {
            if (node.Parent is not { } parent || node.GetMove(size) is not { Point: { } played } move)
            {
                continue;
            }

            StudyPin? pin = study.Pins
                .Where(p => categories is null || categories.Contains(p.Category))
                .OrderBy(p => Priority(p.Category))
                .FirstOrDefault(p => Priority(p.Category) < 99);
            if (pin is null)
            {
                continue;
            }

            BoardState before = cursor.GetBoard(parent);
            Point? answer = pin.Category switch
            {
                PinCategory.Mistake or PinCategory.Question => Alternative(study, StudyStore.Read(parent), before, move.Color, played),
                _ => played,
            };
            questions.Add(new QuizQuestion(parent, node, pin, move.Color, played, answer));
        }

        return questions;
    }

    /// <summary>True when the guess is the answer.</summary>
    public static bool IsRight(QuizQuestion question, Point guess) => question.Answer == guess;

    /// <summary>Which pin of a move makes the question, and which pins make none.</summary>
    private static int Priority(PinCategory category) => category switch
    {
        PinCategory.Mistake => 0,
        PinCategory.KeyMoment => 1,
        PinCategory.GoodMove => 2,
        PinCategory.Joseki => 3,
        PinCategory.LifeAndDeath => 4,
        PinCategory.Question => 5,
        _ => 99,
    };

    /// <summary>The first stone of a "what if" sequence for the mover that differs from what was played.</summary>
    private static Point? Alternative(NodeStudy after, NodeStudy before, BoardState position, Stone mover, Point played) =>
        before.Drawings.Concat(after.Drawings)
            .Where(d => d.Kind == DrawingKind.Sequence && d.Points.Count > 0 && d.FirstColor == mover)
            .Select(d => d.Points[0])
            .Where(p => p != played && position.IsOnBoard(p) && position[p] == Stone.Empty)
            .Cast<Point?>()
            .FirstOrDefault();
}
