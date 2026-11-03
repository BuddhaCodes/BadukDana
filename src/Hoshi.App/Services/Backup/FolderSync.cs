using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hoshi.App.Services.Backup;

/// <summary>
/// "Sync with a folder" (desktop): the player picks a folder that Syncthing, Dropbox, Google Drive or OneDrive keeps
/// in step between computers. Each device writes only its own <c>hoshi-&lt;device&gt;.hoshi</c> there when Hoshi closes,
/// and at start merges the other devices' files that changed since it last looked — the same lossless merge as
/// "Import a backup" (preferences are never taken from another device). With one file per device two computers never
/// write the same file, so the sync service has no conflicts to resolve.
/// </summary>
public static class FolderSync
{
    private const string StateFile = "sync-state.json";

    public static string DeviceFile(string deviceId) => $"hoshi-{deviceId}.{BackupPackage.Extension}";

    /// <summary>A readable, stable id for this device: the computer's name and a few random characters.</summary>
    public static string NewDeviceId()
    {
        string name = new string([.. Environment.MachineName.Where(char.IsLetterOrDigit).Take(20)]).ToLowerInvariant();
        return $"{(name.Length > 0 ? name : "device")}-{Guid.NewGuid().ToString("N")[..4]}";
    }

    /// <summary>
    /// At start, before the services read the data folder: merges every other device's file that changed. Reads the
    /// sync settings straight from <c>settings.json</c> (the settings service does not exist yet). Null when sync is off.
    /// </summary>
    public static BackupSummary? PullAtStart(string dataDirectory)
    {
        (string? folder, string? device) = Read(Path.Combine(dataDirectory, "settings.json"));
        return folder is null || device is null || !Directory.Exists(folder) ? null : Pull(dataDirectory, folder, device);
    }

    /// <summary>Merges the other devices' files in <paramref name="folder"/> that changed since the last pull.</summary>
    public static BackupSummary Pull(string dataDirectory, string folder, string deviceId)
    {
        Dictionary<string, DateTime> seen = LoadState(dataDirectory);
        var total = new BackupSummary(0, 0, 0, 0, 0, false);
        string own = DeviceFile(deviceId);
        foreach (string file in Directory.EnumerateFiles(folder, $"hoshi-*.{BackupPackage.Extension}").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            DateTime written = File.GetLastWriteTimeUtc(file);
            if (name == own || (seen.TryGetValue(name, out DateTime last) && last >= written))
            {
                continue;
            }

            string temp = Path.Combine(Path.GetTempPath(), "hoshi-sync-" + Guid.NewGuid().ToString("N"));
            try
            {
                BackupPackage.Stage(file, temp, applyPreferences: false);
                BackupSummary s = BackupPackage.Merge(Path.Combine(temp, BackupPackage.PendingFolder), dataDirectory, applyPreferences: false);
                total = new BackupSummary(total.Games + s.Games, total.JosekiCards + s.JosekiCards, total.ProblemCards + s.ProblemCards,
                    total.Reviews + s.Reviews, total.Collections + s.Collections, false);
                seen[name] = written;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or FormatException)
            {
                // Half-synced or from a newer Hoshi: try again next time.
                System.Diagnostics.Trace.WriteLine($"Hoshi: could not merge {name}: {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
        }

        SaveState(dataDirectory, seen);
        return total;
    }

    /// <summary>When Hoshi closes (or the phone app pauses): this device's file in the folder.</summary>
    public static bool Publish(string dataDirectory, string? folder, string? deviceId, string version)
    {
        if (folder is null || deviceId is null || !Directory.Exists(folder))
        {
            return false;
        }

        // Written next to the target and renamed, so the sync service never uploads half a file.
        string target = Path.Combine(folder, DeviceFile(deviceId));
        string temp = Path.Combine(folder, $".{DeviceFile(deviceId)}.part");
        BackupPackage.Export(dataDirectory, temp, version);
        File.Move(temp, target, overwrite: true);
        return true;
    }

    /// <summary>Publishes with the saved settings (desktop shutdown); false when sync is off or the folder is gone.</summary>
    public static bool PublishIfEnabled(ISettingsService? settings, string? dataDirectory = null)
    {
        AppSettings? s = settings?.Current;
        try
        {
            return s is not null && Publish(dataDirectory ?? AppPaths.DataDirectory, s.SyncFolder, s.DeviceId,
                Updates.UpdatePlatform.Display(Updates.UpdatePlatform.CurrentVersion));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Hoshi: could not write to the sync folder: {ex.Message}");
            return false;
        }
    }

    private static (string? Folder, string? Device) Read(string settingsPath)
    {
        try
        {
            if (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject s)
            {
                return (Value(s, "syncFolder"), Value(s, "deviceId"));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // No sync this time.
        }

        return (null, null);

        static string? Value(JsonObject o, string key) =>
            o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>() is { Length: > 0 } v ? v : null;
    }

    private static Dictionary<string, DateTime> LoadState(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, StateFile);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void SaveState(string dataDirectory, Dictionary<string, DateTime> seen)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, StateFile), JsonSerializer.Serialize(seen));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"Hoshi: could not save the sync state: {ex.Message}");
        }
    }
}
