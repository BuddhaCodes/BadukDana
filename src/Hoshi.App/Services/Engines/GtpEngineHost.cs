using Hoshi.Core.Localization;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Engines;

/// <summary>A GTP engine Hoshi can use: Hoshi's own KataGo (when it has one) or one the user added.</summary>
public sealed record EngineChoice(string Name, GtpEngineConfig Config, bool IsBuiltIn)
{
    /// <summary>The id saved in the settings: the user's name, or a fixed id for Hoshi's KataGo (its name is translated).</summary>
    public string Id => IsBuiltIn ? GtpEngineHost.BuiltInId : Name;

    public override string ToString() => Name;
}

/// <summary>One line of the GTP console.</summary>
public sealed record ConsoleLine(string Engine, GtpDirection Direction, string Text, DateTime Time);

/// <summary>
/// Runs the GTP engines (one process per engine, started on first use and kept alive), lists them, and keeps the
/// GTP conversation for the console.
/// </summary>
public interface IGtpEngineHost
{
    /// <summary>The engines available now: Hoshi's KataGo first (if any), then the user's, in their order.</summary>
    IReadOnlyList<EngineChoice> Engines { get; }

    /// <summary>The console's lines (oldest first; at most a few thousand).</summary>
    IReadOnlyList<ConsoleLine> ConsoleLines { get; }

    /// <summary>Raised on any thread for each console line.</summary>
    event EventHandler<ConsoleLine>? ConsoleLineAdded;

    /// <summary>The list of engines changed (Preferences).</summary>
    event EventHandler? EnginesChanged;

    /// <summary>The engine with this <see cref="EngineChoice.Id"/>, or null.</summary>
    EngineChoice? Find(string? id);

    /// <summary>The running engine, starting it (handshake included) when needed.</summary>
    Task<GtpEngine> AcquireAsync(EngineChoice engine, CancellationToken cancellationToken = default);

    /// <summary>Stops an engine (it starts again on next use), e.g. after its settings changed.</summary>
    Task StopAsync(string id);

    /// <summary>Re-reads the engine list (after Preferences saved it); engines whose command changed restart on next use.</summary>
    void Refresh(AppSettings settings);
}

public sealed class GtpEngineHost : IGtpEngineHost, IAsyncDisposable
{
    public const int MaxConsoleLines = 4000;
    public const string BuiltInId = "hoshi:katago";

    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly string _baseDirectory;
    private readonly string _dataDirectory;
    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<ConsoleLine> _console = [];
    private IReadOnlyList<EngineChoice> _engines = [];

    public GtpEngineHost(ISettingsService settings, ILoggerFactory? loggers = null, string? baseDirectory = null, string? dataDirectory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = (loggers ?? NullLoggerFactory.Instance).CreateLogger("Gtp");
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        Refresh(settings.Current);
    }

    /// <summary>The name of Hoshi's own KataGo in the engine lists.</summary>
    public static string BuiltInName => Tr.T("Engines.HoshiKataGo");

    public IReadOnlyList<EngineChoice> Engines => _engines;

    public IReadOnlyList<ConsoleLine> ConsoleLines
    {
        get
        {
            lock (_console)
            {
                return [.. _console];
            }
        }
    }

    public event EventHandler<ConsoleLine>? ConsoleLineAdded;

    public event EventHandler? EnginesChanged;

    public EngineChoice? Find(string? id) => id is null ? null : _engines.FirstOrDefault(e => e.Id == id);

    /// <summary>Re-reads the engine list (after Preferences saved it); engines whose command changed restart on next use.</summary>
    public void Refresh(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var list = new List<EngineChoice>();
        if (KataGo.KataGoLocator.Resolve(settings, _baseDirectory, _dataDirectory) is { } kataGo && kataGo.Options.Validate() is null)
        {
            list.Add(new EngineChoice(BuiltInName, KataGo.KataGoLocator.GtpEngine(kataGo, _dataDirectory, BuiltInName), IsBuiltIn: true));
        }

        foreach (EngineEntry e in settings.Engines)
        {
            if (!string.IsNullOrWhiteSpace(e.Name) && list.All(c => c.Name != e.Name.Trim()) && e.Name.Trim() != BuiltInId)
            {
                list.Add(new EngineChoice(e.Name.Trim(), new GtpEngineConfig(e.Name.Trim(), e.Executable, e.Arguments, e.InitCommands), IsBuiltIn: false));
            }
        }

        _engines = list;
        EnginesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<GtpEngine> AcquireAsync(EngineChoice engine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_running.TryGetValue(engine.Id, out Running? r))
            {
                if (r.Config == engine.Config && !r.Engine.HasExited)
                {
                    return r.Engine;
                }

                _running.Remove(engine.Id);
                await r.Engine.DisposeAsync();
            }

            string name = engine.Name;
            Add(new ConsoleLine(name, GtpDirection.Log, Tr.F("Engines.Starting", name), DateTime.Now));
            GtpEngine started = await GtpEngine.StartAsync(
                engine.Config,
                _logger,
                (_, t) => Add(new ConsoleLine(name, t.Direction, t.Text, DateTime.Now)),
                cancellationToken);
            _running[engine.Id] = new Running(engine.Config, started);
            _logger.LogInformation("GTP engine {Engine} ready: {Display}", name, started.DisplayName);
            return started;
        }
        catch (EngineException ex)
        {
            Add(new ConsoleLine(engine.Name, GtpDirection.Log, ex.Message, DateTime.Now));
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            if (_running.Remove(id, out Running? r))
            {
                await r.Engine.DisposeAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Add(ConsoleLine line)
    {
        lock (_console)
        {
            _console.Add(line);
            if (_console.Count > MaxConsoleLines)
            {
                _console.RemoveRange(0, _console.Count - MaxConsoleLines);
            }
        }

        ConsoleLineAdded?.Invoke(this, line);
    }

    public async ValueTask DisposeAsync()
    {
        Running[] all;
        await _lock.WaitAsync();
        try
        {
            all = [.. _running.Values];
            _running.Clear();
        }
        finally
        {
            _lock.Release();
        }

        await Task.WhenAll(all.Select(r => r.Engine.DisposeAsync().AsTask()));
    }

    private sealed record Running(GtpEngineConfig Config, GtpEngine Engine);
}
