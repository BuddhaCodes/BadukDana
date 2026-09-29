namespace Hoshi.App.Services;

/// <summary>Well-known per-user folders (settings, logs, cache).</summary>
public static class AppPaths
{
    /// <summary>
    /// %LOCALAPPDATA%\Hoshi on Windows, ~/Library/Application Support/Hoshi on macOS,
    /// $XDG_DATA_HOME/Hoshi (or ~/.local/share/Hoshi) on Linux.
    /// </summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "Hoshi");
}
