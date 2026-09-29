using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services;

/// <summary>User preferences, stored as JSON in the data folder (settings.json).</summary>
public sealed record AppSettings
{
    public string Theme { get; init; } = "night";

    /// <summary>Animated backgrounds and stone/UI animations (off = static, for low-power machines or preference).</summary>
    public bool Animations { get; init; } = true;
}

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
