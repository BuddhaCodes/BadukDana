using System.Diagnostics;
using Hoshi.Engines.KataGo;
using Hoshi.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Music;

/// <summary>The adaptive background music as the view models see it.</summary>
public interface IMusicService
{
    bool IsPlaying { get; }

    /// <summary>Why music cannot play here, or null.</summary>
    string? Problem { get; }

    /// <summary>Current heat, 0–5 (for a small indicator).</summary>
    double Heat { get; }

    void Start();

    void Stop();

    /// <summary>0–1.</summary>
    void SetVolume(double volume);

    /// <summary>The engine's verdict on a move the user just played.</summary>
    void OnVerdict(MoveQuality quality);

    /// <summary>The fight on the board, when there is no engine verdict (live OGS games, no KataGo).</summary>
    void OnBattle(double heat);
}

/// <summary>
/// Renders the lo-fi layers once (in the background, ~1 s), then streams the mix to the OS audio output on its own
/// thread, updating the heat from <see cref="MusicDirector"/> every buffer (~46 ms).
/// </summary>
public sealed class MusicService : IMusicService, IDisposable
{
    private const int FramesPerBuffer = 2048;

    private readonly ILogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MusicDirector _director = new();
    private readonly object _gate = new();
    private Task<float[][]>? _layers;
    private Task<float[]>? _levelUp;
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private MusicMixer? _mixer;
    private double _volume = 0.35;
    private volatile bool _tapeStop;

    public MusicService(ILogger<MusicService>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _director.TapeStop += (_, _) => _tapeStop = true;
    }

    public bool IsPlaying => _thread is not null;

    public string? Problem { get; private set; }

    public double Heat
    {
        get
        {
            lock (_gate)
            {
                return _director.HeatAt(Now);
            }
        }
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null)
            {
                return;
            }

            _layers ??= Task.Run(() => LofiComposer.Render());
            _levelUp ??= Task.Run(() => LofiComposer.RenderLevelUp());
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Hoshi music", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _thread = null;
            _cts?.Cancel();
        }

        thread?.Join(1000);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        if (_mixer is { } m)
        {
            m.Volume = _volume;
        }
    }

    public void OnBattle(double heat)
    {
        lock (_gate)
        {
            _director.OnBattleHeat(heat, Now);
        }
    }

    public void OnVerdict(MoveQuality quality)
    {
        lock (_gate)
        {
            _director.OnVerdict(quality, Now);
        }
    }

    public void Dispose() => Stop();

    private void Run(CancellationToken token)
    {
        IPcmSink? sink = null;
        try
        {
            float[][] layers = _layers!.GetAwaiter().GetResult();
            sink = PcmSinks.Create(LofiComposer.SampleRate, out string? problem);
            if (sink is null)
            {
                Problem = problem;
                _logger.LogWarning("Music unavailable: {Problem}", problem);
                lock (_gate)
                {
                    _thread = null;
                }

                return;
            }

            Problem = null;
            var mixer = new MusicMixer(layers, _levelUp!.GetAwaiter().GetResult()) { Volume = _volume };
            _mixer = mixer;
            var buffer = new short[FramesPerBuffer * 2];
            while (!token.IsCancellationRequested)
            {
                double heat;
                lock (_gate)
                {
                    heat = _director.HeatAt(Now);
                }

                mixer.SetHeat(heat);
                if (_tapeStop)
                {
                    _tapeStop = false;
                    mixer.StartTapeStop();
                }

                mixer.Render(buffer);
                sink.Write(buffer, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Problem = Tr.F("Music.Stopped", ex.Message);
            _logger.LogWarning(ex, "Music stopped");
        }
        finally
        {
            sink?.Dispose();
            _mixer = null;
        }
    }
}
