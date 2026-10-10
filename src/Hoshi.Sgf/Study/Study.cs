using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Core;

namespace Hoshi.Sgf.Study;

/// <summary>What a pin says about a move.</summary>
public enum PinCategory
{
    Mistake,
    GoodMove,
    Question,
    KeyMoment,
    Idea,
    Joseki,
    LifeAndDeath,
    Time,
    Lesson,
}

public enum DrawingKind
{
    /// <summary>From the first point to the second.</summary>
    Arrow,

    /// <summary>The rectangle between two corners.</summary>
    Area,

    /// <summary>A ring on one point.</summary>
    Mark,

    /// <summary>A letter or word on one point (<see cref="StudyDrawing.Text"/>).</summary>
    Label,

    /// <summary>Numbered "what if" stones, alternating colours from <see cref="StudyDrawing.Text"/> ("B" or "W").</summary>
    Sequence,
}

public enum StudyColor
{
    Gold,
    Red,
    Blue,
    Green,
    Purple,
}

/// <summary>An answer in a pin's thread.</summary>
public sealed record StudyReply(string Author, DateTimeOffset Time, string Text);

/// <summary>A note pinned to a move, with its thread of replies.</summary>
public sealed record StudyPin(string Id, PinCategory Category, string Author, DateTimeOffset Time, string Text, IReadOnlyList<StudyReply> Replies)
{
    public bool Equals(StudyPin? other) =>
        other is not null && Id == other.Id && Category == other.Category && Author == other.Author && Time == other.Time
        && Text == other.Text && Replies.SequenceEqual(other.Replies);

    public override int GetHashCode() => HashCode.Combine(Id, Category, Author, Text);
}

/// <summary>A drawing on the board at a move: arrow, area, mark, label or a sequence of "what if" stones.</summary>
public sealed record StudyDrawing(string Id, DrawingKind Kind, string Author, StudyColor Color, IReadOnlyList<Point> Points, string? Text = null)
{
    public bool Equals(StudyDrawing? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && Author == other.Author && Color == other.Color
        && Points.SequenceEqual(other.Points) && Text == other.Text;

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Author, Color);

    /// <summary>The points of an area (the rectangle between its two corners); one point for the other kinds.</summary>
    public IReadOnlyList<Point> AreaPoints()
    {
        if (Kind != DrawingKind.Area || Points.Count < 2)
        {
            return Points.Take(1).ToList();
        }

        var list = new List<Point>();
        for (int y = Math.Min(Points[0].Y, Points[1].Y); y <= Math.Max(Points[0].Y, Points[1].Y); y++)
        {
            for (int x = Math.Min(Points[0].X, Points[1].X); x <= Math.Max(Points[0].X, Points[1].X); x++)
            {
                list.Add(new Point(x, y));
            }
        }

        return list;
    }

    /// <summary>For a sequence: the colour of its first stone.</summary>
    public Stone FirstColor => Text is "W" or "w" ? Stone.White : Stone.Black;
}

/// <summary>Everything studied at one move.</summary>
public sealed record NodeStudy(IReadOnlyList<StudyPin> Pins, IReadOnlyList<StudyDrawing> Drawings)
{
    public static NodeStudy Empty { get; } = new([], []);

    public bool IsEmpty => Pins.Count == 0 && Drawings.Count == 0;
}

public sealed record StudyMergeResult(int Items, int NewMoves);

/// <summary>
/// Stores a game study inside the SGF itself: each node's pins and drawings as compact JSON in the private
/// property <c>HS</c> (other programs keep it and ignore it). For sharing with other programs, <see cref="WriteWithMirror"/>
/// also writes the study as ordinary comments and markup (AR arrows, SQ areas, CR marks, LB labels and numbered
/// sequences), which Hoshi removes again on load (<see cref="StripMirror"/>).
/// </summary>
public static class StudyStore
{
    public const string Property = "HS";

    /// <summary>Line that separates the user's comment from the mirrored study in <c>C</c>.</summary>
    public const string MirrorMarker = "— Hoshi study —";

    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(8);
        foreach (byte b in bytes)
        {
            sb.Append(Alphabet[b % Alphabet.Length]);
        }

        return sb.ToString();
    }

    public static NodeStudy Read(GameNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.GetValue(Property) is not { Length: > 0 } json)
        {
            return NodeStudy.Empty;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return NodeStudy.Empty;
            }

            var pins = new List<StudyPin>();
            foreach (JsonNode? p in root["pins"] as JsonArray ?? [])
            {
                if (p is JsonObject o && o["id"]?.GetValue<string>() is { } id)
                {
                    var replies = new List<StudyReply>();
                    foreach (JsonNode? r in o["re"] as JsonArray ?? [])
                    {
                        if (r is JsonObject ro)
                        {
                            replies.Add(new StudyReply(Str(ro, "by"), Time(ro), Str(ro, "text")));
                        }
                    }

                    PinCategory category = Enum.TryParse(Str(o, "cat"), out PinCategory c) ? c : PinCategory.Question;
                    pins.Add(new StudyPin(id, category, Str(o, "by"), Time(o), Str(o, "text"), replies));
                }
            }

            var drawings = new List<StudyDrawing>();
            foreach (JsonNode? d in root["draw"] as JsonArray ?? [])
            {
                if (d is JsonObject o && o["id"]?.GetValue<string>() is { } id && Enum.TryParse(Str(o, "kind"), out DrawingKind kind))
                {
                    StudyColor color = Enum.TryParse(Str(o, "col"), out StudyColor col) ? col : StudyColor.Gold;
                    var points = new List<Point>();
                    foreach (JsonNode? pt in o["pts"] as JsonArray ?? [])
                    {
                        if (Point.TryParseSgf(pt?.GetValue<string>(), out Point point))
                        {
                            points.Add(point);
                        }
                    }

                    if (points.Count > 0)
                    {
                        drawings.Add(new StudyDrawing(id, kind, Str(o, "by"), color, points, o["text"]?.GetValue<string>()));
                    }
                }
            }

            return new NodeStudy(pins, drawings);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return NodeStudy.Empty;
        }
    }

    public static void Write(GameNode node, NodeStudy study)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(study);
        if (study.IsEmpty)
        {
            node.RemoveProperty(Property);
            return;
        }

        var pins = new JsonArray();
        foreach (StudyPin p in study.Pins)
        {
            var o = new JsonObject { ["id"] = p.Id, ["cat"] = p.Category.ToString(), ["by"] = p.Author, ["at"] = Stamp(p.Time), ["text"] = p.Text };
            if (p.Replies.Count > 0)
            {
                o["re"] = new JsonArray([.. p.Replies.Select(r => (JsonNode)new JsonObject { ["by"] = r.Author, ["at"] = Stamp(r.Time), ["text"] = r.Text })]);
            }

            pins.Add(o);
        }

        var drawings = new JsonArray();
        foreach (StudyDrawing d in study.Drawings)
        {
            var o = new JsonObject
            {
                ["id"] = d.Id,
                ["kind"] = d.Kind.ToString(),
                ["by"] = d.Author,
                ["col"] = d.Color.ToString(),
                ["pts"] = new JsonArray([.. d.Points.Select(p => (JsonNode)p.ToSgf())]),
            };
            if (d.Text is not null)
            {
                o["text"] = d.Text;
            }

            drawings.Add(o);
        }

        var root = new JsonObject { ["v"] = 1 };
        if (pins.Count > 0)
        {
            root["pins"] = pins;
        }

        if (drawings.Count > 0)
        {
            root["draw"] = drawings;
        }

        node.SetValue(Property, root.ToJsonString());
    }

    /// <summary>Every node with a study, in tree order (main line first at each branch).</summary>
    public static IEnumerable<(GameNode Node, NodeStudy Study)> All(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        foreach (GameNode n in tree.Root.Descendants())
        {
            if (n.HasProperty(Property) && Read(n) is { IsEmpty: false } s)
            {
                yield return (n, s);
            }
        }
    }

    /// <summary>Everyone who wrote in the study, in order of appearance.</summary>
    public static IReadOnlyList<string> Authors(GameTree tree) =>
    [
        .. All(tree)
            .SelectMany(x => x.Study.Pins.SelectMany(p => p.Replies.Select(r => r.Author).Prepend(p.Author)).Concat(x.Study.Drawings.Select(d => d.Author)))
            .Where(a => a.Length > 0)
            .Distinct(),
    ];

    /// <summary>The SGF text with the study also written as comments and markup for other programs; the tree is left unchanged.</summary>
    public static string WriteWithMirror(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var added = new List<(GameNode Node, string? Comment, List<(string Id, string Value)> Values)>();
        foreach ((GameNode node, NodeStudy study) in All(tree).ToList())
        {
            string? before = node.Comment;
            var values = new List<(string, string)>();
            foreach ((string id, string value) in Mirror(study))
            {
                if (!node.GetValues(id).Contains(value))
                {
                    node.AddValue(id, value);
                    values.Add((id, value));
                }
            }

            string text = MirrorText(study);
            if (text.Length > 0)
            {
                node.Comment = (string.IsNullOrWhiteSpace(before) ? string.Empty : before + "\n\n") + MirrorMarker + "\n" + text;
            }

            added.Add((node, before, values));
        }

        try
        {
            return SgfWriter.Write(tree);
        }
        finally
        {
            foreach ((GameNode node, string? comment, List<(string Id, string Value)> values) in added)
            {
                node.Comment = comment;
                foreach ((string id, string value) in values)
                {
                    node.RemoveValue(id, value);
                }
            }
        }
    }

    /// <summary>Removes what <see cref="WriteWithMirror"/> added for other programs (call after opening a file).</summary>
    public static void StripMirror(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        foreach ((GameNode node, NodeStudy study) in All(tree).ToList())
        {
            if (node.Comment is { } c && c.IndexOf(MirrorMarker, StringComparison.Ordinal) is var at and >= 0)
            {
                node.Comment = c[..at].TrimEnd();
            }

            foreach ((string id, string value) in Mirror(study))
            {
                node.RemoveValue(id, value);
            }
        }
    }

    /// <summary>
    /// Adds another person's study to this game: their pins, drawings and replies go to the same moves (found by the
    /// moves from the start); moves missing here are added as variations. Items already present (same id) are kept.
    /// </summary>
    public static StudyMergeResult Merge(GameTree target, GameTree source, bool createMissingMoves = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        int items = 0;
        int newMoves = 0;
        int size = Math.Max(target.Info.Width, target.Info.Height);
        foreach ((GameNode from, NodeStudy theirs) in All(source).ToList())
        {
            GameNode? to = target.Root;
            foreach (GameNode step in GameCursor.Path(from).Skip(1))
            {
                SgfMove? move = step.GetMove(size);
                GameNode? next = to.Children.FirstOrDefault(c => move is null ? !c.HasMove : c.GetMove(size) == move);
                if (next is null && !createMissingMoves)
                {
                    to = null;
                    break;
                }

                if (next is null)
                {
                    next = to.AddChild();
                    if (move is { } m)
                    {
                        next.SetValue(m.Color == Stone.White ? "W" : "B", m.Point?.ToSgf() ?? string.Empty);
                        newMoves++;
                    }
                }

                to = next;
            }

            if (to is null)
            {
                continue; // that move is not in the target (e.g. taken back online)
            }

            NodeStudy mine = Read(to);
            var pins = mine.Pins.ToList();
            foreach (StudyPin p in theirs.Pins)
            {
                int i = pins.FindIndex(x => x.Id == p.Id);
                if (i < 0)
                {
                    pins.Add(p);
                    items++;
                    continue;
                }

                var replies = pins[i].Replies.ToList();
                foreach (StudyReply r in p.Replies.Where(r => !replies.Contains(r)))
                {
                    replies.Add(r);
                    items++;
                }

                pins[i] = pins[i] with { Replies = [.. replies.OrderBy(r => r.Time)] };
            }

            var drawings = mine.Drawings.ToList();
            foreach (StudyDrawing d in theirs.Drawings.Where(d => drawings.All(x => x.Id != d.Id)))
            {
                drawings.Add(d);
                items++;
            }

            Write(to, new NodeStudy(pins, drawings));
        }

        return new StudyMergeResult(items, newMoves);
    }

    /// <summary>Plain-text name of a category (used in files for other programs, which are not translated).</summary>
    public static string EnglishName(PinCategory category) => category switch
    {
        PinCategory.GoodMove => "Good move",
        PinCategory.KeyMoment => "Key moment",
        PinCategory.LifeAndDeath => "Life & death",
        _ => category.ToString(),
    };

    private static IEnumerable<(string Id, string Value)> Mirror(NodeStudy study)
    {
        foreach (StudyDrawing d in study.Drawings)
        {
            switch (d.Kind)
            {
                case DrawingKind.Arrow when d.Points.Count >= 2:
                    yield return ("AR", d.Points[0].ToSgf() + ":" + d.Points[1].ToSgf());
                    break;
                case DrawingKind.Area:
                    foreach (Point p in d.AreaPoints())
                    {
                        yield return ("SQ", p.ToSgf());
                    }

                    break;
                case DrawingKind.Mark:
                    yield return ("CR", d.Points[0].ToSgf());
                    break;
                case DrawingKind.Label:
                    yield return ("LB", d.Points[0].ToSgf() + ":" + (d.Text ?? "?"));
                    break;
                case DrawingKind.Sequence:
                    for (int i = 0; i < d.Points.Count; i++)
                    {
                        yield return ("LB", d.Points[i].ToSgf() + ":" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }
    }

    private static string MirrorText(NodeStudy study)
    {
        var sb = new StringBuilder();
        foreach (StudyPin p in study.Pins)
        {
            sb.Append(EnglishName(p.Category)).Append(" (").Append(p.Author).Append(')');
            if (p.Text.Length > 0)
            {
                sb.Append(": ").Append(p.Text);
            }

            sb.Append('\n');
            foreach (StudyReply r in p.Replies)
            {
                sb.Append("  ↳ ").Append(r.Author).Append(": ").Append(r.Text).Append('\n');
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : string.Empty;

    private static DateTimeOffset Time(JsonObject o) =>
        DateTimeOffset.TryParse(Str(o, "at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset t) ? t : DateTimeOffset.UnixEpoch;

    private static string Stamp(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);
}
