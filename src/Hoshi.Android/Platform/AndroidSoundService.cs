using Android.Content;
using Android.Media;
using Hoshi.App.Services;

namespace Hoshi.Droid;

/// <summary>
/// Board sounds with Android's SoundPool: every effect is loaded once (the files come from
/// <see cref="SystemSoundService"/>, which also honours the user's own sounds) and plays with low latency.
/// </summary>
internal sealed class AndroidSoundService : ISoundService, IDisposable
{
    private readonly SoundPool _pool;
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<int> _ready = [];
    private SystemSoundService? _files;

    public AndroidSoundService(Context context)
    {
        _ = context;
        AudioAttributes attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Game)!
            .SetContentType(AudioContentType.Sonification)!
            .Build()!;
        _pool = new SoundPool.Builder()
            .SetMaxStreams(8)!
            .SetAudioAttributes(attributes)!
            .Build()!;
        _pool.LoadComplete += (_, e) =>
        {
            lock (_ready)
            {
                _ready.Add(e.SampleId);
            }
        };
    }

    /// <summary>Where the sound files come from; loads them all so the first stone already clicks.</summary>
    public AndroidSoundService WithFiles(SystemSoundService files)
    {
        _files = files;
        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            int variants = effect is SoundEffect.Stone or SoundEffect.StoneFirm ? 3 : 1;
            for (int i = 0; i < variants; i++)
            {
                Load(files.SoundFile(effect));
            }
        }

        return this;
    }

    public void Play(SoundEffect effect, double volume)
    {
        if (_files is null || volume <= 0)
        {
            return;
        }

        try
        {
            int id = Load(_files.SoundFile(effect));
            bool ready;
            lock (_ready)
            {
                ready = _ready.Contains(id);
            }

            if (ready)
            {
                float v = (float)Math.Clamp(volume, 0, 1);
                _pool.Play(id, v, v, 1, 0, 1f);
            }
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.Exception)
        {
            Android.Util.Log.Warn("Hoshi", $"Could not play {effect}: {ex.Message}");
        }
    }

    private int Load(string file)
    {
        lock (_ids)
        {
            if (!_ids.TryGetValue(file, out int id))
            {
                id = _pool.Load(file, 1);
                _ids[file] = id;
            }

            return id;
        }
    }

    public void Dispose() => _pool.Release();
}
