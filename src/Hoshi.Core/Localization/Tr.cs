using System.Globalization;

namespace Hoshi.Core.Localization;

/// <summary>
/// Hoshi's user-facing text in English (default), Spanish, Japanese, Korean and Chinese (simplified). English and
/// Spanish live next to the code (<c>Strings.*.cs</c>); the other languages are key → text tables embedded as JSON
/// (<c>Translations/*.json</c>), and a key they lack falls back to English. Pure lookup with no UI dependency, so every layer
/// can use it: <c>Tr.T("Main.Pass")</c>, or <c>Tr.F("Ogs.TokenRejected", error)</c> for formatted text
/// (invariant culture, so numbers read the same in both languages). Each layer adds its entries in a partial file
/// (<c>Strings.*.cs</c>); a missing key shows the key itself, which tests catch.
/// </summary>
public static partial class Tr
{
    public const string English = "en";
    public const string Spanish = "es";
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string Chinese = "zh";

    private static readonly Dictionary<string, (string En, string Es)> Entries = Build();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Extra = new(StringComparer.Ordinal)
    {
        [Japanese] = LoadTable(Japanese),
        [Korean] = LoadTable(Korean),
        [Chinese] = LoadTable(Chinese),
    };
    private static volatile string _language = English;

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<string> Languages { get; } = [English, Spanish, Japanese, Korean, Chinese];

    /// <summary>Each language's name in itself, for the language picker.</summary>
    public static string NativeName(string language) => language switch
    {
        Spanish => "Español",
        Japanese => "日本語",
        Korean => "한국어",
        Chinese => "简体中文",
        _ => "English",
    };

    /// <summary>Dates and month names in the current language.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language switch
    {
        Spanish => "es-ES",
        Japanese => "ja-JP",
        Korean => "ko-KR",
        Chinese => "zh-CN",
        _ => "en-GB",
    });

    /// <summary>
    /// Fonts to try after the theme's own for a CJK language, so Han characters take the language's own shapes
    /// (Japanese and Chinese draw some differently) instead of whatever the system falls back to.
    /// </summary>
    public static string CjkFontFallback => _language switch
    {
        Japanese => "Yu Gothic UI, Meiryo, Hiragino Sans, Noto Sans CJK JP, Noto Sans JP",
        Korean => "Malgun Gothic, Apple SD Gothic Neo, Noto Sans CJK KR, Noto Sans KR",
        Chinese => "Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, Noto Sans SC",
        _ => string.Empty,
    };

    /// <summary>How many of the keys a table language has translated (1 for English and Spanish).</summary>
    public static double Coverage(string language) =>
        Extra.TryGetValue(language, out IReadOnlyDictionary<string, string>? table) ? (double)Entries.Keys.Count(table.ContainsKey) / Entries.Count : 1;

    public static string Language => _language;

    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    /// <summary>Switches language ("en", "es", "ja", "ko", "zh"; anything else means English).</summary>
    public static void SetLanguage(string? language)
    {
        string normalized = Normalize(language);
        if (normalized == _language)
        {
            return;
        }

        _language = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Normalize(string? language) =>
        language is null ? English
        : Languages.FirstOrDefault(l => language.StartsWith(l, StringComparison.OrdinalIgnoreCase)) ?? English;

    public static string T(string key) => T(key, _language);

    public static string T(string key, string language)
    {
        if (!Entries.TryGetValue(key, out (string En, string Es) e))
        {
            return key;
        }

        return language switch
        {
            English => e.En,
            Spanish => e.Es,
            _ => Extra.TryGetValue(language, out IReadOnlyDictionary<string, string>? table) && table.TryGetValue(key, out string? text) ? text : e.En,
        };
    }

    /// <summary>The text a table language has for <paramref name="key"/>, or null (tests and tools).</summary>
    public static string? Translated(string key, string language) =>
        Extra.TryGetValue(language, out IReadOnlyDictionary<string, string>? table) && table.TryGetValue(key, out string? text) ? text : null;

    /// <summary>The keys a table language holds (to find stale ones in tests).</summary>
    public static IReadOnlyCollection<string> TableKeys(string language) =>
        Extra.TryGetValue(language, out IReadOnlyDictionary<string, string>? table) ? [.. table.Keys] : [];

    private static IReadOnlyDictionary<string, string> LoadTable(string language)
    {
        using Stream? stream = typeof(Tr).Assembly.GetManifestResourceStream($"Hoshi.Core.Localization.Translations.{language}.json");
        return stream is null
            ? new Dictionary<string, string>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }

    public static bool Has(string key) => Entries.ContainsKey(key);

    public static string F(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

    private static Dictionary<string, (string En, string Es)> Build()
    {
        var d = new Dictionary<string, (string En, string Es)>(StringComparer.Ordinal);
        AddCore(d);
        AddEngines(d);
        AddOgs(d);
        AddApp(d);
        AddViews(d);
        AddEngineUi(d);
        AddStudy(d);
        AddPhone(d);
        AddTsumego(d);
        AddShare(d);
        AddLibrary(d);
        return d;
    }

    static partial void AddCore(Dictionary<string, (string En, string Es)> d);

    static partial void AddEngines(Dictionary<string, (string En, string Es)> d);

    static partial void AddOgs(Dictionary<string, (string En, string Es)> d);

    static partial void AddApp(Dictionary<string, (string En, string Es)> d);

    static partial void AddViews(Dictionary<string, (string En, string Es)> d);

    static partial void AddEngineUi(Dictionary<string, (string En, string Es)> d);

    static partial void AddStudy(Dictionary<string, (string En, string Es)> d);

    static partial void AddPhone(Dictionary<string, (string En, string Es)> d);

    static partial void AddTsumego(Dictionary<string, (string En, string Es)> d);

    static partial void AddShare(Dictionary<string, (string En, string Es)> d);

    static partial void AddLibrary(Dictionary<string, (string En, string Es)> d);

    /// <summary>Adds one entry; a duplicate key throws, so mistakes surface in the first test.</summary>
    private static void Add(Dictionary<string, (string En, string Es)> d, string key, string en, string es) => d.Add(key, (en, es));
}
