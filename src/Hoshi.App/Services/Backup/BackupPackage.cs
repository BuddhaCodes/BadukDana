using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.Services.Backup;

/// <summary>What an import brought in (for the status line and the log).</summary>
public sealed record BackupSummary(int Games, int JosekiCards, int ProblemCards, int Reviews, int Collections, bool Preferences)
{
    public bool IsEmpty => Games + JosekiCards + ProblemCards + Reviews + Collections == 0 && !Preferences;
}

/// <summary>
/// A <c>.hoshi</c> file: a zip of the player's own data — played games and their studies, joseki and problem
/// progress, problems from their games, KataGo review counts and (optionally applied) portable preferences.
/// Never inside: OGS tokens (<c>secrets/</c>), logs, caches, KataGo and engine paths, window sizes.
/// <para>
/// Importing merges instead of replacing, so a phone and a desktop can swap files both ways: games join by id
/// (the newer wins), spaced-repetition cards by id (the latest review wins), reviews by game, SGF collections by
/// content. The services keep their data in memory, so an import is staged (<see cref="Stage"/>) and merged at the
/// next start, before anything reads the files (<see cref="ApplyPending"/>).
/// </para>
/// </summary>
public static class BackupPackage
{
    public const string Extension = "hoshi";
    public const int Format = 1;
    public const string PendingFolder = "import-pending";
    private const string Manifest = "hoshi-backup.json";
    private const string OptionsFile = "import-options.json";

    /// <summary>Preferences that mean the same on every device (no paths, sizes or per-device choices).</summary>
    public static readonly IReadOnlySet<string> PortableSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "theme", "themeChoice", "boardSkin", "stoneSkin", "backgroundSkin", "language", "animations", "moveEffects", "effects",
        "soundVolume", "stoneSounds", "atariAlerts", "music", "musicVolume", "josekiHints", "chatSounds", "studyAuthor",
        "moveNumbers", "zenSilence", "shareHideNames", "shareShowBrand", "humanLevel", "engineClock", "welcomeSeen",
    };

    private static readonly string[] Files = ["tsumego-progress.json", "reviews.json", "settings.json"];
    private static readonly string[] Folders = ["replays", "joseki", "tsumego"];
    private static readonly JsonSerializerOptions Camel = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions Plain = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Writes the backup of <paramref name="dataDirectory"/> to <paramref name="zipPath"/>.</summary>
    public static void Export(string dataDirectory, string zipPath, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        string temp = zipPath + ".part";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var manifest = new JsonObject { ["format"] = Format, ["app"] = "Hoshi", ["version"] = version, ["created"] = DateTimeOffset.UtcNow };
            using (var w = new StreamWriter(zip.CreateEntry(Manifest).Open()))
            {
                w.Write(manifest.ToJsonString(Camel));
            }

            foreach (string folder in Folders)
            {
                string source = Path.Combine(dataDirectory, folder);
                if (!Directory.Exists(source))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".sgf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
            }

            foreach (string file in Files[..2])
            {
                string path = Path.Combine(dataDirectory, file);
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, file, CompressionLevel.Optimal);
                }
            }

            if (PortablePreferences(Path.Combine(dataDirectory, "settings.json")) is { } prefs)
            {
                using var w = new StreamWriter(zip.CreateEntry("settings.json").Open());
                w.Write(prefs.ToJsonString(Camel));
            }
        }

        File.Move(temp, zipPath, overwrite: true);
    }

    /// <summary>
    /// Checks <paramref name="zipPath"/> and unpacks it into the data folder's <c>import-pending</c> for the next start.
    /// Throws <see cref="InvalidDataException"/> when it is not a Hoshi backup (or a newer format).
    /// </summary>
    public static void Stage(string zipPath, string dataDirectory, bool applyPreferences)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry manifest = zip.GetEntry(Manifest) ?? throw new InvalidDataException("Not a Hoshi backup.");
        using (var reader = new StreamReader(manifest.Open()))
        {
            JsonNode? m = JsonNode.Parse(reader.ReadToEnd());
            if (m?["app"]?.GetValue<string>() != "Hoshi" || (m["format"]?.GetValue<int>() ?? 0) is < 1 or > Format)
            {
                throw new InvalidDataException("Not a Hoshi backup, or one from a newer Hoshi.");
            }
        }

        string pending = Path.Combine(dataDirectory, PendingFolder);
        if (Directory.Exists(pending))
        {
            Directory.Delete(pending, recursive: true);
        }

        Directory.CreateDirectory(pending);
        string root = Path.GetFullPath(pending) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(pending, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
            {
                continue; // folders, and nothing may land outside the pending folder ("zip slip")
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        File.WriteAllText(Path.Combine(pending, OptionsFile), new JsonObject { ["preferences"] = applyPreferences }.ToJsonString());
    }

    /// <summary>True when an import waits for the next start.</summary>
    public static bool HasPending(string dataDirectory) => Directory.Exists(Path.Combine(dataDirectory, PendingFolder));

    /// <summary>Merges a staged import into the data folder (call before any service reads it). Null when none.</summary>
    public static BackupSummary? ApplyPending(string dataDirectory)
    {
        string pending = Path.Combine(dataDirectory, PendingFolder);
        if (!Directory.Exists(pending))
        {
            return null;
        }

        bool preferences = false;
        string options = Path.Combine(pending, OptionsFile);
        if (File.Exists(options))
        {
            preferences = JsonNode.Parse(File.ReadAllText(options))?["preferences"]?.GetValue<bool>() == true;
        }

        BackupSummary summary = Merge(pending, dataDirectory, preferences);
        Directory.Delete(pending, recursive: true);
        return summary;
    }

    /// <summary>Merges the unpacked backup in <paramref name="source"/> into <paramref name="target"/>.</summary>
    public static BackupSummary Merge(string source, string target, bool applyPreferences)
    {
        int games = MergeReplays(Path.Combine(source, "replays"), Path.Combine(target, "replays"));

        int joseki = MergeJson<JosekiCard, string>(Path.Combine(source, "joseki", "progress.json"), Path.Combine(target, "joseki", "progress.json"), Camel,
            c => c.Id, (a, b) => (b.LastReview ?? DateTimeOffset.MinValue, b.Reviews).CompareTo((a.LastReview ?? DateTimeOffset.MinValue, a.Reviews)) > 0);

        int problems = MergeDictionary<TsumegoCardDto>(Path.Combine(source, "tsumego-progress.json"), Path.Combine(target, "tsumego-progress.json"),
            (a, b) => (b.LastSolved ?? DateTimeOffset.MinValue, b.Solved + b.Failed).CompareTo((a.LastSolved ?? DateTimeOffset.MinValue, a.Solved + a.Failed)) > 0);

        int reviews = MergeJson<ReviewDto, string>(Path.Combine(source, "reviews.json"), Path.Combine(target, "reviews.json"), Plain,
            r => r.Key, (a, b) => b.Reviewed > a.Reviewed);

        int collections = 0;
        foreach (string folder in new[] { "joseki", "tsumego" })
        {
            string from = Path.Combine(source, folder);
            if (!Directory.Exists(from))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(from, "*.sgf", SearchOption.AllDirectories))
            {
                collections += MergeCollection(file, Path.Combine(target, Path.GetRelativePath(source, file)));
            }
        }

        bool prefs = applyPreferences && MergePreferences(Path.Combine(source, "settings.json"), Path.Combine(target, "settings.json"));
        return new BackupSummary(games, joseki, problems, reviews, collections, prefs);
    }

    private static JsonObject? PortablePreferences(string settingsPath)
    {
        if (!File.Exists(settingsPath) || JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject all)
        {
            return null;
        }

        var portable = new JsonObject();
        foreach ((string key, JsonNode? value) in all)
        {
            if (PortableSettings.Contains(key))
            {
                portable[key] = value?.DeepClone();
            }
        }

        return portable;
    }

    private static bool MergePreferences(string from, string to)
    {
        if (PortablePreferences(from) is not { Count: > 0 } incoming)
        {
            return false;
        }

        JsonObject local = File.Exists(to) && JsonNode.Parse(File.ReadAllText(to)) is JsonObject o ? o : [];
        foreach ((string key, JsonNode? value) in incoming)
        {
            // Keep the local file's spelling of the key (camelCase today).
            string existing = local.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            local[existing] = value?.DeepClone();
        }

        WriteAtomically(to, local.ToJsonString(Camel));
        return true;
    }

    private static int MergeReplays(string from, string to)
    {
        string incomingIndex = Path.Combine(from, "index.json");
        if (!File.Exists(incomingIndex))
        {
            return 0;
        }

        List<ReplayEntry> incoming = JsonSerializer.Deserialize<List<ReplayEntry>>(File.ReadAllText(incomingIndex), Camel) ?? [];
        string localIndex = Path.Combine(to, "index.json");
        List<ReplayEntry> local = File.Exists(localIndex) ? JsonSerializer.Deserialize<List<ReplayEntry>>(File.ReadAllText(localIndex), Camel) ?? [] : [];
        Directory.CreateDirectory(to);
        int added = 0;
        foreach (ReplayEntry entry in incoming)
        {
            string file = Path.Combine(from, Path.GetFileName(entry.FileName));
            if (!File.Exists(file))
            {
                continue;
            }

            int at = local.FindIndex(e => e.Id == entry.Id);
            if (at >= 0 && local[at].Played >= entry.Played)
            {
                continue; // ours is as new or newer
            }

            string name = at >= 0 ? local[at].FileName : Unique(to, Path.GetFileName(entry.FileName));
            File.Copy(file, Path.Combine(to, name), overwrite: true);
            var merged = entry with { FileName = name };
            if (at >= 0)
            {
                local[at] = merged;
            }
            else
            {
                local.Add(merged);
            }

            added++;
        }

        if (added > 0)
        {
            WriteAtomically(localIndex, JsonSerializer.Serialize(local, Camel));
        }

        return added;
    }

    private static int MergeJson<T, TKey>(string from, string to, JsonSerializerOptions options, Func<T, TKey> key, Func<T, T, bool> incomingWins)
        where TKey : notnull
    {
        if (!File.Exists(from))
        {
            return 0;
        }

        List<T> incoming = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(from), options) ?? [];
        Dictionary<TKey, T> local = File.Exists(to)
            ? (JsonSerializer.Deserialize<List<T>>(File.ReadAllText(to), options) ?? []).ToDictionary(key)
            : [];
        int changed = 0;
        foreach (T item in incoming)
        {
            if (!local.TryGetValue(key(item), out T? mine) || incomingWins(mine, item))
            {
                local[key(item)] = item;
                changed++;
            }
        }

        if (changed > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            WriteAtomically(to, JsonSerializer.Serialize(local.Values.ToList(), options));
        }

        return changed;
    }

    private static int MergeDictionary<T>(string from, string to, Func<T, T, bool> incomingWins)
    {
        if (!File.Exists(from))
        {
            return 0;
        }

        Dictionary<string, T> incoming = JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(from), Plain) ?? [];
        Dictionary<string, T> local = File.Exists(to) ? JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(to), Plain) ?? [] : [];
        int changed = 0;
        foreach ((string id, T card) in incoming)
        {
            if (!local.TryGetValue(id, out T? mine) || incomingWins(mine, card))
            {
                local[id] = card;
                changed++;
            }
        }

        if (changed > 0)
        {
            WriteAtomically(to, JsonSerializer.Serialize(local));
        }

        return changed;
    }

    /// <summary>SGF collections (joseki lines, problems from games): games are added when their text is new.</summary>
    private static int MergeCollection(string from, string to)
    {
        if (!File.Exists(to))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
            return 1;
        }

        List<GameTree> mine = [.. SgfParser.ParseCollection(File.ReadAllText(to)).Games];
        var known = new HashSet<string>(mine.Select(g => SgfWriter.Write(g)), StringComparer.Ordinal);
        int before = mine.Count;
        foreach (GameTree game in SgfParser.ParseCollection(File.ReadAllText(from)).Games)
        {
            if (known.Add(SgfWriter.Write(game)))
            {
                mine.Add(game);
            }
        }

        if (mine.Count == before)
        {
            return 0;
        }

        WriteAtomically(to, SgfWriter.Write(mine));
        return 1;
    }

    private static string Unique(string folder, string name)
    {
        string candidate = name;
        for (int i = 2; File.Exists(Path.Combine(folder, candidate)); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}";
        }

        return candidate;
    }

    private static void WriteAtomically(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    // The files' own shapes, kept loose on purpose: a backup from an older Hoshi may lack newer fields.
    private sealed record TsumegoCardDto(int Box, DateTimeOffset Due, int Solved, int Failed, DateTimeOffset? LastSolved);

    private sealed record ReviewDto(string Key, DateTimeOffset Played, DateTimeOffset Reviewed, JsonElement Black, JsonElement White);
}
