using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hoshi.Sgf;

public sealed class SgfParseResult
{
    internal SgfParseResult(IReadOnlyList<GameTree> games, IReadOnlyList<string> warnings)
    {
        Games = games;
        Warnings = warnings;
    }

    public IReadOnlyList<GameTree> Games { get; }

    /// <summary>Problems that were recovered from (the parser never throws on malformed input).</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Tolerant SGF (FF[1]–FF[4]) parser. Unknown properties are kept verbatim; malformed input is recovered
/// where possible and reported through <see cref="SgfParseResult.Warnings"/>.
/// </summary>
public static partial class SgfParser
{
    static SgfParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Parses the first game of a collection.</summary>
    public static GameTree Parse(string text) =>
        ParseCollection(text).Games.FirstOrDefault() ?? throw new FormatException("The text does not contain an SGF game.");

    public static GameTree Parse(byte[] data) =>
        ParseCollection(data).Games.FirstOrDefault() ?? throw new FormatException("The data does not contain an SGF game.");

    /// <summary>
    /// Decodes bytes using, in order: a byte-order mark, the <c>CA</c> property, strict UTF-8, and finally
    /// ISO-8859-1 (the SGF default).
    /// </summary>
    public static SgfParseResult ParseCollection(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ParseCollection(Decode(data));
    }

    public static SgfParseResult ParseCollection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        return reader.ReadCollection();
    }

    internal static string Decode(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            using var sr = new StreamReader(new MemoryStream(data), detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }

        string ascii = Encoding.Latin1.GetString(data);
        Match ca = CharsetRegex().Match(ascii);
        if (ca.Success)
        {
            try
            {
                return Encoding.GetEncoding(ca.Groups[1].Value.Trim()).GetString(data);
            }
            catch (ArgumentException)
            {
                // Unknown charset name: fall through to the heuristics below.
            }
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return ascii;
        }
    }

    [GeneratedRegex(@"CA\s*\[([^\]]*)\]")]
    private static partial Regex CharsetRegex();

    private sealed class Reader(string text)
    {
        private readonly List<string> _warnings = [];
        private int _pos;

        public SgfParseResult ReadCollection()
        {
            var games = new List<GameTree>();
            while (true)
            {
                int start = text.IndexOf('(', _pos);
                if (start < 0)
                {
                    break;
                }

                if (games.Count == 0 && text.AsSpan(_pos, start - _pos).Trim().Length > 0)
                {
                    Warn(0, "Ignored text before the first game");
                }

                _pos = start;
                GameNode? root = ReadGameTree(parent: null);
                if (root is not null)
                {
                    games.Add(new GameTree(root));
                }
            }

            return new SgfParseResult(games, _warnings);
        }

        /// <summary>Reads "(" sequence tree* ")" and returns the first node of the sequence.</summary>
        private GameNode? ReadGameTree(GameNode? parent)
        {
            _pos++; // '('
            GameNode? first = null;
            GameNode? last = parent;

            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length)
                {
                    Warn(_pos, "Missing ')' at end of input");
                    return first;
                }

                char c = text[_pos];
                if (c == ';')
                {
                    _pos++;
                    var node = new GameNode();
                    ReadProperties(node);
                    if (last is null)
                    {
                        first = node;
                    }
                    else
                    {
                        last.AddChild(node);
                        first ??= node;
                    }

                    last = node;
                }
                else if (c == '(')
                {
                    if (last is null)
                    {
                        // A variation before any node: treat its nodes as belonging here.
                        Warn(_pos, "Variation without a preceding node");
                        GameNode? inner = ReadGameTree(null);
                        first ??= inner;
                        last = inner;
                    }
                    else
                    {
                        ReadGameTree(last);
                    }
                }
                else if (c == ')')
                {
                    _pos++;
                    return first;
                }
                else
                {
                    Warn(_pos, $"Unexpected character '{c}'");
                    _pos++;
                }
            }
        }

        private void ReadProperties(GameNode node)
        {
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length || !char.IsAsciiLetter(text[_pos]))
                {
                    return;
                }

                int idStart = _pos;
                var id = new StringBuilder();
                while (_pos < text.Length && char.IsAsciiLetter(text[_pos]))
                {
                    if (char.IsAsciiLetterUpper(text[_pos]))
                    {
                        id.Append(text[_pos]);
                    }

                    _pos++;
                }

                var values = new List<string>();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= text.Length || text[_pos] != '[')
                    {
                        break;
                    }

                    values.Add(ReadValue());
                }

                if (id.Length == 0)
                {
                    Warn(idStart, "Property identifier without capital letters");
                    continue;
                }

                if (values.Count == 0)
                {
                    Warn(idStart, $"Property {id} has no value");
                    continue;
                }

                foreach (string v in values)
                {
                    node.AddValue(id.ToString(), v);
                }
            }
        }

        private string ReadValue()
        {
            int start = _pos;
            _pos++; // '['
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                char c = text[_pos];
                if (c == ']')
                {
                    _pos++;
                    return sb.ToString();
                }

                if (c == '\\' && _pos + 1 < text.Length)
                {
                    char next = text[_pos + 1];
                    if (next is '\n' or '\r')
                    {
                        // Soft line break: remove the backslash and the (possibly two-character) newline.
                        _pos += 2;
                        if (_pos < text.Length && text[_pos] is '\n' or '\r' && text[_pos] != next)
                        {
                            _pos++;
                        }

                        continue;
                    }

                    sb.Append(next);
                    _pos += 2;
                    continue;
                }

                if (c == '\r')
                {
                    // Normalise CRLF / CR to LF inside text.
                    sb.Append('\n');
                    _pos += _pos + 1 < text.Length && text[_pos + 1] == '\n' ? 2 : 1;
                    continue;
                }

                sb.Append(c);
                _pos++;
            }

            Warn(start, "Unterminated property value");
            return sb.ToString();
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }

        private void Warn(int position, string message) =>
            _warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{message} (at offset {position})."));
    }
}
