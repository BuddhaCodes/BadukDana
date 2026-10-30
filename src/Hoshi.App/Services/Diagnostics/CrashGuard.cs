using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Diagnostics;

/// <summary>What went wrong the last time, kept on disk until the player reports or dismisses it.</summary>
public sealed record CrashRecord(DateTimeOffset When, string Version, string Context, string Type, string Message, string Stack, bool Fatal);

/// <summary>
/// Last line of defence: unhandled exceptions (any thread, the UI dispatcher, unobserved tasks) end in the log
/// instead of vanishing, and the most serious one is kept in <c>logs/last-crash.json</c> so the next start can offer
/// "Report a problem". Nothing leaves the computer unless the player opens the report and sends it.
/// </summary>
public static class CrashGuard
{
    private const string FileName = "last-crash.json";
    private static readonly object Gate = new();
    private static ILogger _logger = NullLogger.Instance;
    private static string? _directory;
    private static bool _installed;

    /// <summary>The version written into crash records.</summary>
    public static string Version { get; set; } = typeof(CrashGuard).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Hooks the process-wide handlers once. <paramref name="logDirectory"/> holds <c>last-crash.json</c>.</summary>
    public static void Install(ILogger logger, string logDirectory, bool hookDispatcher = true)
    {
        lock (Gate)
        {
            _logger = logger;
            _directory = logDirectory;
            if (_installed)
            {
                return;
            }

            _installed = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Record(ex, "Unhandled exception", fatal: e.IsTerminating);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "Unobserved task");
            e.SetObserved();
        };

        if (hookDispatcher)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // A command or event handler failed. Keep the app alive (the game must not be lost), but remember it.
                Record(e.Exception, "UI thread", fatal: false);
                e.Handled = true;
            };
        }
    }

    /// <summary>Logs an exception that was caught at a boundary (an async void handler, say) and remembers it.</summary>
    public static void Report(Exception exception, string context) => Record(exception, context, fatal: false);

    /// <summary>The record left by the last run (or this one), if any.</summary>
    public static CrashRecord? Pending()
    {
        string? path = PathOrNull();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), DiagnosticsJson.Default.CrashRecord);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Forgets the pending record (reported or dismissed).</summary>
    public static void Clear()
    {
        try
        {
            if (PathOrNull() is { } path && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete the crash record");
        }
    }

    private static void Record(Exception exception, string context, bool fatal)
    {
        try
        {
            _logger.Log(fatal ? LogLevel.Critical : LogLevel.Error, exception, "{Context}: unhandled {Type}", context, exception.GetType().Name);
            if (PathOrNull() is not { } path)
            {
                return;
            }

            // A fatal crash always wins; a minor one never overwrites a fatal one not yet reported.
            if (!fatal && Pending() is { Fatal: true })
            {
                return;
            }

            var record = new CrashRecord(DateTimeOffset.UtcNow, Version, context, exception.GetType().FullName ?? exception.GetType().Name,
                ProblemReport.Scrub(exception.Message), ProblemReport.Scrub(exception.ToString()), fatal);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(record, DiagnosticsJson.Default.CrashRecord));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Trace.WriteLine($"Hoshi: could not record a crash: {ex.Message}");
        }
    }

    private static string? PathOrNull()
    {
        lock (Gate)
        {
            return _directory is null ? null : Path.Combine(_directory, FileName);
        }
    }

    /// <summary>For tests: forget the handlers' target folder.</summary>
    internal static void ResetForTests(string? directory)
    {
        lock (Gate)
        {
            _directory = directory;
            _logger = NullLogger.Instance;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(CrashRecord))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class DiagnosticsJson : System.Text.Json.Serialization.JsonSerializerContext;
