using Hoshi.Core;

namespace Hoshi.Sgf.Problems;

/// <summary>Writes problems as SGF games, in the convention <see cref="TsumegoProblem"/> reads.</summary>
public static class TsumegoWriter
{
    /// <summary>
    /// "Find the best move" from a game: the position before the move as setup stones, <c>PL</c> for who plays, the
    /// better move marked RIGHT and the move that was played marked WRONG (with an explanation after the mark).
    /// </summary>
    public static GameTree BestMove(BoardState before, Stone toPlay, Point best, Point? played, string name, string? instructions = null, string? wrongExplanation = null, string? rightExplanation = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (toPlay == Stone.Empty || !before.IsOnBoard(best))
        {
            throw new ArgumentException("A problem needs a player and an answer on the board.", nameof(best));
        }

        GameTree tree = GameTree.Create(Math.Max(before.Width, before.Height), before.Rules);
        GameNode root = tree.Root;
        var black = new List<string>();
        var white = new List<string>();
        for (int y = 0; y < before.Height; y++)
        {
            for (int x = 0; x < before.Width; x++)
            {
                Stone s = before[x, y];
                if (s == Stone.Black)
                {
                    black.Add(new Point(x, y).ToSgf());
                }
                else if (s == Stone.White)
                {
                    white.Add(new Point(x, y).ToSgf());
                }
            }
        }

        if (black.Count > 0)
        {
            root.SetValues("AB", black);
        }

        if (white.Count > 0)
        {
            root.SetValues("AW", white);
        }

        string color = toPlay == Stone.White ? "W" : "B";
        root.SetValue("PL", color);
        tree.Info.GameName = name;
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            root.Comment = instructions;
        }

        GameNode right = root.AddChild();
        right.SetValue(color, best.ToSgf());
        right.Comment = string.IsNullOrWhiteSpace(rightExplanation) ? "RIGHT" : "RIGHT\n" + rightExplanation;
        if (played is { } p && p != best && before.IsOnBoard(p))
        {
            GameNode wrong = root.AddChild();
            wrong.SetValue(color, p.ToSgf());
            wrong.Comment = string.IsNullOrWhiteSpace(wrongExplanation) ? "WRONG" : "WRONG\n" + wrongExplanation;
        }

        return tree;
    }
}
