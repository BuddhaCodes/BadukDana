using System.Globalization;

namespace Hoshi.Core.Localization;

/// <summary>
/// Hoshi's user-facing text in English (default) and Spanish. Pure lookup with no UI dependency, so every layer
/// can use it: <c>Tr.T("Main.Pass")</c>, or <c>Tr.F("Ogs.TokenRejected", error)</c> for formatted text
/// (invariant culture, so numbers read the same in both languages). Each layer adds its entries in a partial file
/// (<c>Strings.*.cs</c>); a missing key shows the key itself, which tests catch.
/// </summary>
public static partial class Tr
{
    public const string English = "en";
    public const string Spanish = "es";

    private static readonly Dictionary<string, (string En, string Es)> Entries = Build();
    private static volatile string _language = English;

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<string> Languages { get; } = [English, Spanish];

    public static string Language => _language;

    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    /// <summary>Switches language ("en" or "es"; anything else means English).</summary>
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
        language is not null && language.StartsWith(Spanish, StringComparison.OrdinalIgnoreCase) ? Spanish : English;

    public static string T(string key) => T(key, _language);

    public static string T(string key, string language) =>
        Entries.TryGetValue(key, out (string En, string Es) e) ? (language == Spanish ? e.Es : e.En) : key;

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
        return d;
    }

    static partial void AddCore(Dictionary<string, (string En, string Es)> d);

    static partial void AddEngines(Dictionary<string, (string En, string Es)> d);

    static partial void AddOgs(Dictionary<string, (string En, string Es)> d);

    static partial void AddApp(Dictionary<string, (string En, string Es)> d);

    static partial void AddViews(Dictionary<string, (string En, string Es)> d);

    static partial void AddEngineUi(Dictionary<string, (string En, string Es)> d);

    static partial void AddStudy(Dictionary<string, (string En, string Es)> d);

    /// <summary>Adds one entry; a duplicate key throws, so mistakes surface in the first test.</summary>
    private static void Add(Dictionary<string, (string En, string Es)> d, string key, string en, string es) => d.Add(key, (en, es));
}
