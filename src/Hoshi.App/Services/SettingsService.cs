using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services;

/// <summary>User preferences, stored as JSON in the data folder (settings.json).</summary>
public sealed record AppSettings
{
    public string Theme { get; init; } = "night";

    /// <summary>Goban, stones and background chosen on top of the theme (ids from <c>Themes.Skins</c>); null = the theme's.</summary>
    public string? BoardSkin { get; init; }

    public string? StoneSkin { get; init; }

    public string? BackgroundSkin { get; init; }

    /// <summary>User interface language: "en" (English, the default) or "es" (Spanish).</summary>
    public string Language { get; init; } = "en";

    /// <summary>Animated backgrounds and stone/UI animations (off = static, for low-power machines or preference).</summary>
    public bool Animations { get; init; } = true;

    /// <summary>KataGo analysis engine: executable, neural network (.bin.gz) and analysis config (.cfg).</summary>
    public string? KataGoExecutable { get; init; }

    public string? KataGoModel { get; init; }

    public string? KataGoConfig { get; init; }

    /// <summary>GTP engines added in Preferences → Engines (to play against, engine vs engine, analysis).</summary>
    public IReadOnlyList<EngineEntry> Engines { get; init; } = [];

    /// <summary>The engine of the analysis panel: null = Hoshi's KataGo (analysis engine), otherwise a GTP engine's name.</summary>
    public string? AnalysisEngine { get; init; }

    /// <summary>Search visits per analysed position (more = stronger and slower).</summary>
    public int AnalysisVisits { get; init; } = 200;

    /// <summary>Celebrate strong moves (by KataGo's judgement) with a sound and a board impact.</summary>
    public bool MoveEffects { get; init; } = true;

    /// <summary>
    /// How much the board moves: explosions and impacts of strong moves, shattering captures and the atari alert.
    /// Off = none, Subtle = a small flash, a gentle fade and the sweat drop only, Full = everything.
    /// </summary>
    public EffectsLevel Effects { get; init; } = EffectsLevel.Full;

    /// <summary>Sound effects volume, 0–100 (0 = silent; the board effect still plays).</summary>
    public int SoundVolume { get; init; } = 70;

    /// <summary>A soft "pachi" when a stone is placed (also when stepping forward through a game).</summary>
    public bool StoneSounds { get; init; } = true;

    /// <summary>Comic atari alert: groups with one liberty tremble and sweat, with an "uh-oh" when it happens.</summary>
    public bool AtariAlerts { get; init; } = true;

    /// <summary>Adaptive lo-fi music that heats up with streaks of good moves (needs the analysis for that).</summary>
    public bool Music { get; init; } = true;

    /// <summary>Music volume, 0–100.</summary>
    public int MusicVolume { get; init; } = 35;

    /// <summary>All sound off (effects and music) from the speaker button, keeping each volume for later.</summary>
    public bool Muted { get; init; }

    /// <summary>Show known joseki continuations on the main board (J).</summary>
    public bool JosekiHints { get; init; }

    /// <summary>"Not now" on the Install KataGo card (Preferences → Analysis still offers it).</summary>
    public bool KataGoPromptDismissed { get; init; }

    /// <summary>A soft sound when the opponent writes in the game chat.</summary>
    public bool ChatSounds { get; init; } = true;

    /// <summary>Ask GitHub for a newer Hoshi shortly after start and every 12 hours.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>A version the user chose to skip (e.g. "0.1.7"); newer ones are offered again.</summary>
    public string? SkippedUpdate { get; init; }

    /// <summary>The version that ran last time (to thank the player once after an update).</summary>
    public string? LastRunVersion { get; init; }
}

/// <summary>How strong the visual effects on the board are (Preferences, the bottom bar, the sound panel and F).</summary>
public enum EffectsLevel
{
    Off,
    Subtle,
    Full,
}

/// <summary>A GTP engine as saved in the settings (see <see cref="Hoshi.Engines.Gtp.GtpEngineConfig"/>).</summary>
public sealed record EngineEntry(string Name, string Executable, string Arguments = "", string InitCommands = "");

public interface ISettingsService
{
    AppSettings Current { get; }

    void Save(AppSettings settings);
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path;
    private readonly ILogger _logger;

    public JsonSettingsService(string? path = null, ILogger<JsonSettingsService>? logger = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "settings.json");
        _logger = logger ?? (ILogger)NullLogger.Instance;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not save settings: {Error}", ex.Message);
        }
    }

    private AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("Could not read settings, using defaults: {Error}", ex.Message);
            return new AppSettings();
        }
    }
}
