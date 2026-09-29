using Hoshi.Core;

namespace Hoshi.Sgf;

/// <summary>A move stored in a node: colour plus point, or a pass when <see cref="Point"/> is null.</summary>
public readonly record struct SgfMove(Stone Color, Point? Point)
{
    public bool IsPass => Point is null;
}

public enum MarkupKind
{
    Triangle,
    Square,
    Circle,
    Cross,
    Label,
}

/// <summary>SGF markup on a point (TR, SQ, CR, MA, LB).</summary>
public sealed record Markup(Point Point, MarkupKind Kind, string? Text = null);

/// <summary>
/// A node of an SGF game tree: an ordered set of properties (each with one or more raw, unescaped values)
/// and an ordered list of children. The first child is the main line.
/// </summary>
public sealed class GameNode
{
    internal static readonly (string Id, MarkupKind Kind)[] MarkupProperties =
    [
        ("TR", MarkupKind.Triangle),
        ("SQ", MarkupKind.Square),
        ("CR", MarkupKind.Circle),
        ("MA", MarkupKind.Cross),
    ];

    private readonly OrderedDictionary<string, List<string>> _properties = new(StringComparer.Ordinal);
    private readonly List<GameNode> _children = [];

    public GameNode? Parent { get; private set; }

    public IReadOnlyList<GameNode> Children => _children;

    public bool IsRoot => Parent is null;

    public IEnumerable<string> PropertyIds => _properties.Keys;

    public bool HasProperty(string id) => _properties.ContainsKey(id);

    public IReadOnlyList<string> GetValues(string id) =>
        _properties.TryGetValue(id, out List<string>? values) ? values : [];

    public string? GetValue(string id) =>
        _properties.TryGetValue(id, out List<string>? values) && values.Count > 0 ? values[0] : null;

    public void SetValue(string id, string value) => SetValues(id, [value]);

    public void SetValues(string id, IEnumerable<string> values)
    {
        ValidateId(id);
        var list = values.ToList();
        if (list.Count == 0)
        {
            _properties.Remove(id);
        }
        else
        {
            _properties[id] = list;
        }
    }

    public void AddValue(string id, string value)
    {
        ValidateId(id);
        if (_properties.TryGetValue(id, out List<string>? values))
        {
            values.Add(value);
        }
        else
        {
            _properties[id] = [value];
        }
    }

    /// <summary>Removes one value; the property disappears when its last value is removed.</summary>
    public bool RemoveValue(string id, string value)
    {
        if (!_properties.TryGetValue(id, out List<string>? values) || !values.Remove(value))
        {
            return false;
        }

        if (values.Count == 0)
        {
            _properties.Remove(id);
        }

        return true;
    }

    public bool RemoveProperty(string id) => _properties.Remove(id);

    public GameNode AddChild(GameNode? child = null) => InsertChild(_children.Count, child ?? new GameNode());

    public GameNode InsertChild(int index, GameNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("The node already has a parent.");
        }

        _children.Insert(index, child);
        child.Parent = this;
        return child;
    }

    public void MoveChild(GameNode child, int newIndex)
    {
        if (!_children.Remove(child))
        {
            throw new ArgumentException("Not a child of this node.", nameof(child));
        }

        _children.Insert(Math.Clamp(newIndex, 0, _children.Count), child);
    }

    /// <summary>Removes this node (and its subtree) from its parent.</summary>
    public void Detach()
    {
        Parent?._children.Remove(this);
        Parent = null;
    }

    /// <summary>This node and all its descendants, depth first, main line first.</summary>
    public IEnumerable<GameNode> Descendants()
    {
        var stack = new Stack<GameNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            GameNode n = stack.Pop();
            yield return n;
            for (int i = n._children.Count - 1; i >= 0; i--)
            {
                stack.Push(n._children[i]);
            }
        }
    }

    public SgfMove? GetMove(int boardSize)
    {
        foreach ((string id, Stone color) in new[] { ("B", Stone.Black), ("W", Stone.White) })
        {
            if (_properties.TryGetValue(id, out List<string>? values))
            {
                string value = values.Count > 0 ? values[0] : string.Empty;
                if (Point.IsSgfPass(value, boardSize))
                {
                    return new SgfMove(color, null);
                }

                return Point.TryParseSgf(value, out Point p) ? new SgfMove(color, p) : null;
            }
        }

        return null;
    }

    public bool HasMove => HasProperty("B") || HasProperty("W");

    /// <summary>Points of a point-list property, expanding compressed rectangles such as <c>aa:cc</c>.</summary>
    public IReadOnlyList<Point> GetPoints(string id)
    {
        var points = new List<Point>();
        foreach (string value in GetValues(id))
        {
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                if (Point.TryParseSgf(value, out Point p))
                {
                    points.Add(p);
                }

                continue;
            }

            if (Point.TryParseSgf(value[..colon], out Point a) && Point.TryParseSgf(value[(colon + 1)..], out Point b))
            {
                for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                {
                    for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    {
                        points.Add(new Point(x, y));
                    }
                }
            }
        }

        return points;
    }

    public IReadOnlyList<Markup> GetMarkup()
    {
        var markup = new List<Markup>();
        foreach ((string id, MarkupKind kind) in MarkupProperties)
        {
            markup.AddRange(GetPoints(id).Select(p => new Markup(p, kind)));
        }

        foreach (string value in GetValues("LB"))
        {
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon == 2 && Point.TryParseSgf(value[..2], out Point p))
            {
                markup.Add(new Markup(p, MarkupKind.Label, value[3..]));
            }
        }

        return markup;
    }

    public string? Comment
    {
        get => GetValue("C");
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RemoveProperty("C");
            }
            else
            {
                SetValue("C", value);
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"'{id}' is not a valid SGF property identifier.", nameof(id));
        }
    }
}
