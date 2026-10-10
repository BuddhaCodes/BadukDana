using System.Globalization;
using System.Text;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Services.Study;

/// <summary>KataGo's view of a pinned move, for the report.</summary>
public sealed record StudyVerdict(MoveQuality Quality, double PointsLost, Point? Best);

/// <summary>
/// The game report: a self-contained HTML page (no scripts, no external files) with the game's details, the pins
/// by category, every pinned moment as a small board with its drawings, the notes and their replies, KataGo's
/// verdict on those moments when it was available, and the lessons. Easy to print, save or send.
/// </summary>
public static class StudyReport
{
    public static string Html(GameTree tree, IReadOnlyDictionary<GameNode, StudyVerdict>? verdicts = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        int width = tree.Info.Width, height = tree.Info.Height;
        var cursor = new GameCursor(tree);
        var moments = StudyStore.All(tree).ToList();
        string black = tree.Info.BlackPlayer ?? Tr.T("Common.Black");
        string white = tree.Info.WhitePlayer ?? Tr.T("Common.White");
        string title = Tr.F("Study.ReportTitle", black, white);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Tr.Language).Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(E(title)).Append("</title><style>").Append(Css).Append("</style></head><body><main>");
        sb.Append("<header><p class=\"eyebrow\">Hoshi · ").Append(E(Tr.T("Study.Report"))).Append("</p><h1>").Append(E(title)).Append("</h1><p class=\"meta\">");
        var meta = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{width}×{height}") };
        if (tree.Info.Komi is { } komi)
        {
            meta.Add(string.Create(CultureInfo.InvariantCulture, $"komi {komi:0.#}"));
        }

        if (tree.Info.Result is { Length: > 0 } result)
        {
            meta.Add(result);
        }

        if (tree.Info.Date is { Length: > 0 } date)
        {
            meta.Add(date);
        }

        IReadOnlyList<string> authors = StudyStore.Authors(tree);
        if (authors.Count > 0)
        {
            meta.Add(Tr.F("Study.ReportAuthors", string.Join(", ", authors)));
        }

        sb.Append(E(string.Join(" · ", meta))).Append("</p></header>");

        // Summary by category.
        var pins = moments.SelectMany(m => m.Study.Pins.Select(p => (m.Node, Pin: p))).ToList();
        sb.Append("<section class=\"summary\">");
        foreach (IGrouping<PinCategory, (GameNode Node, StudyPin Pin)> g in pins.GroupBy(p => p.Pin.Category).OrderBy(g => g.Key))
        {
            sb.Append("<div class=\"chip\" style=\"--c:").Append(Colour(g.Key)).Append("\"><b>").Append(g.Count())
                .Append("</b> ").Append(E(CategoryName(g.Key))).Append("</div>");
        }

        if (pins.Count == 0)
        {
            sb.Append("<p>").Append(E(Tr.T("Study.ReportEmpty"))).Append("</p>");
        }

        sb.Append("</section>");

        // Lessons first: what to take away.
        var lessons = pins.Where(p => p.Pin.Category == PinCategory.Lesson && p.Pin.Text.Length > 0).ToList();
        if (lessons.Count > 0)
        {
            sb.Append("<section class=\"lessons\"><h2>").Append(E(Tr.T("Study.Lessons"))).Append("</h2><ul>");
            foreach ((GameNode node, StudyPin pin) in lessons)
            {
                sb.Append("<li>").Append(E(pin.Text)).Append(" <span class=\"muted\">— ").Append(E(MoveName(cursor, node, height))).Append("</span></li>");
            }

            sb.Append("</ul></section>");
        }

        sb.Append("<section class=\"moments\">");
        foreach ((GameNode node, NodeStudy study) in moments)
        {
            BoardState board = cursor.GetBoard(node);
            SgfMove? move = node.GetMove(Math.Max(width, height));
            sb.Append("<article><div class=\"board\">").Append(Svg(board, move?.Point, study.Drawings)).Append("</div><div class=\"notes\">");
            sb.Append("<h3>").Append(E(MoveName(cursor, node, height))).Append("</h3>");
            if (verdicts is not null && verdicts.TryGetValue(node, out StudyVerdict? v))
            {
                sb.Append("<p class=\"verdict q-").Append(v.Quality.ToString().ToLowerInvariant()).Append("\">KataGo: ").Append(E(QualityName(v.Quality)));
                if (v.PointsLost >= 0.05)
                {
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $" (−{v.PointsLost:0.0})"));
                }

                if (v.Best is { } best && best != move?.Point)
                {
                    sb.Append(" · ").Append(E(Tr.F("Study.ReportBest", best.ToHuman(height))));
                }

                sb.Append("</p>");
            }

            foreach (StudyPin pin in study.Pins)
            {
                sb.Append("<div class=\"pin\" style=\"--c:").Append(Colour(pin.Category)).Append("\"><p><span class=\"tag\">").Append(E(CategoryName(pin.Category)))
                    .Append("</span> <span class=\"by\">").Append(E(pin.Author)).Append("</span></p>");
                if (pin.Text.Length > 0)
                {
                    sb.Append("<p class=\"text\">").Append(E(pin.Text)).Append("</p>");
                }

                foreach (StudyReply r in pin.Replies)
                {
                    sb.Append("<p class=\"reply\"><span class=\"by\">").Append(E(r.Author)).Append("</span> ").Append(E(r.Text)).Append("</p>");
                }

                sb.Append("</div>");
            }

            if (study.Pins.Count == 0)
            {
                sb.Append("<p class=\"muted\">").Append(E(Tr.T("Study.ReportDrawingsOnly"))).Append("</p>");
            }

            sb.Append("</div></article>");
        }

        sb.Append("</section><footer>").Append(E(Tr.F("Study.ReportFooter", (now ?? DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))
            .Append("</footer></main></body></html>");
        return sb.ToString();
    }

    public static string CategoryName(PinCategory category) => Tr.T("Study.Cat." + category);

    /// <summary>The colour of a category (pins, timeline, report).</summary>
    public static string Colour(PinCategory category) => category switch
    {
        PinCategory.Mistake => "#d6403a",
        PinCategory.GoodMove => "#2e9e54",
        PinCategory.Question => "#3b82d6",
        PinCategory.KeyMoment => "#d9a12a",
        PinCategory.Idea => "#8c50c8",
        PinCategory.Joseki => "#1f9e9a",
        PinCategory.LifeAndDeath => "#e0702a",
        PinCategory.Time => "#8a8f9e",
        _ => "#c2577f",
    };

    private static string MoveName(GameCursor cursor, GameNode node, int height)
    {
        int number = GameCursor.Path(node).Count(n => n.HasMove);
        SgfMove? move = node.GetMove(Math.Max(cursor.Tree.Info.Width, height));
        if (move is not { } m)
        {
            return number == 0 ? Tr.T("Study.Start") : Tr.F("Study.AfterMove", number);
        }

        string colour = Tr.T(m.Color == Stone.Black ? "Common.Black" : "Common.White");
        string where = m.Point is { } p ? p.ToHuman(height) : Tr.T("Game.PassNoun");
        return Tr.F("Study.MoveName", number, colour, where);
    }

    private static string QualityName(MoveQuality q) => q switch
    {
        MoveQuality.Best => Tr.T("Analysis.BestMove"),
        MoveQuality.Excellent => Tr.T("Analysis.Excellent"),
        MoveQuality.Good => Tr.T("Analysis.Good"),
        MoveQuality.Inaccuracy => Tr.T("Analysis.Inaccuracy"),
        MoveQuality.Mistake => Tr.T("Analysis.Mistake"),
        _ => Tr.T("Analysis.Blunder"),
    };

    /// <summary>A small board as SVG: wood, grid, star points, stones, the last move and the study drawings.</summary>
    internal static string Svg(BoardState board, Point? last, IReadOnlyList<StudyDrawing> drawings)
    {
        const double cell = 14, margin = 12;
        double w = (2 * margin) + ((board.Width - 1) * cell), h = (2 * margin) + ((board.Height - 1) * cell);
        string X(int x) => (margin + (x * cell)).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(int y) => (margin + (y * cell)).ToString("0.#", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {w:0.#} {h:0.#}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\"><rect width=\"100%\" height=\"100%\" rx=\"6\" fill=\"#e6c27d\"/>");
        sb.Append("<g stroke=\"#5a4321\" stroke-width=\".7\">");
        for (int i = 0; i < board.Width; i++)
        {
            sb.Append("<line x1=\"").Append(X(i)).Append("\" y1=\"").Append(Y(0)).Append("\" x2=\"").Append(X(i)).Append("\" y2=\"").Append(Y(board.Height - 1)).Append("\"/>");
        }

        for (int j = 0; j < board.Height; j++)
        {
            sb.Append("<line x1=\"").Append(X(0)).Append("\" y1=\"").Append(Y(j)).Append("\" x2=\"").Append(X(board.Width - 1)).Append("\" y2=\"").Append(Y(j)).Append("\"/>");
        }

        sb.Append("</g>");
        foreach (StudyDrawing d in drawings.Where(d => d.Kind == DrawingKind.Area))
        {
            IReadOnlyList<Point> pts = d.AreaPoints();
            int x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{margin + (x0 * cell) - 6.5:0.#}\" y=\"{margin + (y0 * cell) - 6.5:0.#}\" width=\"{((x1 - x0) * cell) + 13:0.#}\" height=\"{((y1 - y0) * cell) + 13:0.#}\" rx=\"3\" fill=\"{Hex(d.Color)}\" fill-opacity=\".22\" stroke=\"{Hex(d.Color)}\" stroke-dasharray=\"3 2\"/>");
        }

        foreach (Point p in board.AllPoints.Where(p => board[p] != Stone.Empty))
        {
            bool black = board[p] == Stone.Black;
            sb.Append("<circle cx=\"").Append(X(p.X)).Append("\" cy=\"").Append(Y(p.Y)).Append("\" r=\"6.4\" fill=\"").Append(black ? "#1d1d1f" : "#f5f2ea")
                .Append("\" stroke=\"").Append(black ? "#000" : "#9b968a").Append("\" stroke-width=\".6\"/>");
        }

        if (last is { } lm && board.IsOnBoard(lm) && board[lm] != Stone.Empty)
        {
            sb.Append("<circle cx=\"").Append(X(lm.X)).Append("\" cy=\"").Append(Y(lm.Y)).Append("\" r=\"2.6\" fill=\"none\" stroke=\"").Append(board[lm] == Stone.Black ? "#fff" : "#000").Append("\" stroke-width=\"1.1\"/>");
        }

        foreach (StudyDrawing d in drawings)
        {
            string col = Hex(d.Color);
            switch (d.Kind)
            {
                case DrawingKind.Arrow when d.Points.Count >= 2:
                {
                    double ax = margin + (d.Points[0].X * cell), ay = margin + (d.Points[0].Y * cell);
                    double bx = margin + (d.Points[1].X * cell), by = margin + (d.Points[1].Y * cell);
                    double len = Math.Max(0.001, Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay))));
                    double ux = (bx - ax) / len, uy = (by - ay) / len;
                    double hx = bx - (ux * 6), hy = by - (uy * 6);
                    sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{ax:0.#}\" y1=\"{ay:0.#}\" x2=\"{hx:0.#}\" y2=\"{hy:0.#}\" stroke=\"{col}\" stroke-width=\"2.2\" stroke-linecap=\"round\"/>");
                    sb.Append(CultureInfo.InvariantCulture, $"<polygon points=\"{bx:0.#},{by:0.#} {hx - (uy * 4):0.#},{hy + (ux * 4):0.#} {hx + (uy * 4):0.#},{hy - (ux * 4):0.#}\" fill=\"{col}\"/>");
                    break;
                }

                case DrawingKind.Mark:
                    sb.Append("<circle cx=\"").Append(X(d.Points[0].X)).Append("\" cy=\"").Append(Y(d.Points[0].Y)).Append("\" r=\"5\" fill=\"none\" stroke=\"").Append(col).Append("\" stroke-width=\"1.8\"/>");
                    break;
                case DrawingKind.Label:
                    sb.Append("<circle cx=\"").Append(X(d.Points[0].X)).Append("\" cy=\"").Append(Y(d.Points[0].Y)).Append("\" r=\"5.4\" fill=\"").Append(col).Append("\"/>");
                    sb.Append("<text x=\"").Append(X(d.Points[0].X)).Append("\" y=\"").Append(Y(d.Points[0].Y)).Append("\" dy=\"2.6\" font-size=\"7\" text-anchor=\"middle\" fill=\"#fff\" font-family=\"sans-serif\">").Append(E(d.Text ?? "?")).Append("</text>");
                    break;
                case DrawingKind.Sequence:
                    Stone s = d.FirstColor;
                    for (int i = 0; i < d.Points.Count; i++)
                    {
                        Point p = d.Points[i];
                        bool black = s == Stone.Black;
                        sb.Append("<circle cx=\"").Append(X(p.X)).Append("\" cy=\"").Append(Y(p.Y)).Append("\" r=\"6\" fill=\"").Append(black ? "#1d1d1f" : "#f5f2ea").Append("\" fill-opacity=\".75\" stroke=\"").Append(col).Append("\" stroke-width=\"1\"/>");
                        sb.Append("<text x=\"").Append(X(p.X)).Append("\" y=\"").Append(Y(p.Y)).Append("\" dy=\"2.5\" font-size=\"7\" text-anchor=\"middle\" fill=\"").Append(black ? "#fff" : "#000").Append("\" font-family=\"sans-serif\">")
                            .Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("</text>");
                        s = black ? Stone.White : Stone.Black;
                    }

                    break;
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Hex(StudyColor color) => color switch
    {
        StudyColor.Red => "#d6403a",
        StudyColor.Blue => "#2e6ed6",
        StudyColor.Green => "#289650",
        StudyColor.Purple => "#8c50c8",
        _ => "#d6961e",
    };

    /// <summary>HTML escaping that keeps accents and symbols readable (the page is UTF-8).</summary>
    private static string E(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    private const string Css = """
        :root{--ink:#16181f;--muted:#6d6a63;--paper:#f6f1e7;--card:#fffdf8;--line:#e3dccd}
        *{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.55 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
        main{max-width:980px;margin:0 auto;padding:32px 20px 60px}
        .eyebrow{letter-spacing:.18em;text-transform:uppercase;font-size:12px;color:var(--muted);margin:0}
        h1{font:600 30px/1.2 Georgia,'Times New Roman',serif;margin:6px 0}.meta,.muted{color:var(--muted)}
        h2{font:600 20px Georgia,serif;margin:0 0 10px}h3{margin:0 0 8px;font-size:16px}
        .summary{display:flex;flex-wrap:wrap;gap:8px;margin:22px 0}
        .chip{border:1px solid var(--line);background:var(--card);border-left:5px solid var(--c);border-radius:999px;padding:5px 14px}
        .lessons{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px 20px;margin-bottom:22px}
        .lessons ul{margin:0;padding-left:20px}
        article{display:grid;grid-template-columns:minmax(180px,300px) 1fr;gap:20px;background:var(--card);border:1px solid var(--line);border-radius:12px;padding:16px;margin-bottom:14px;break-inside:avoid}
        .board svg{width:100%;height:auto;display:block}
        .pin{border-left:4px solid var(--c);padding:2px 0 2px 12px;margin:8px 0}.pin p{margin:2px 0}
        .tag{color:var(--c);font-weight:700}.by{font-weight:600}.reply{color:#3d3a35;padding-left:14px;border-left:2px solid var(--line)}
        .verdict{display:inline-block;background:#efe8d9;border-radius:6px;padding:2px 10px;margin:0 0 6px}
        .q-best,.q-excellent{background:#dcefe1}.q-mistake,.q-blunder{background:#f6dcd9}.q-inaccuracy{background:#f6ecd0}
        footer{margin-top:30px;color:var(--muted);font-size:13px}
        @media (max-width:640px){article{grid-template-columns:1fr}}
        @media print{body{background:#fff}article,.lessons,.chip{border-color:#ccc}}
        """;
}
