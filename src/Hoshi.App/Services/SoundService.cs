using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services;

/// <summary>Hoshi's sound effects (synthesised by <c>tools/gen_sounds.py</c>).</summary>
public enum SoundEffect
{
    /// <summary>A good move: a soft wooden thump.</summary>
    ImpactSmall,

    /// <summary>An excellent move: a boom with debris.</summary>
    ExplosionMedium,

    /// <summary>The engine's best move: a cinematic explosion.</summary>
    ExplosionBig,

    /// <summary>Any stone placed: a soft "pachi" with a faint chime (three variants, rotated).</summary>
    Stone,

    /// <summary>One or two stones captured: crack, gathered clacks, a two-note chime.</summary>
    CaptureSmall,

    /// <summary>Three or more stones captured: a bigger shatter, more clacks, an arpeggio and a low thock.</summary>
    CaptureBig,
}

public interface ISoundService
{
    /// <summary>Plays an effect without blocking; volume is 0–1. Failures are logged once, never thrown.</summary>
    void Play(SoundEffect effect, double volume);
}

/// <summary>
/// Plays sounds with what each OS already has, so Hoshi needs no audio library:
/// Windows MCI (winmm), <c>afplay</c> on macOS, <c>paplay</c>/<c>aplay</c> on Linux.
/// A file named like the effect (<c>explosion_big.wav</c> or <c>.mp3</c>) in <see cref="CustomDirectory"/> replaces
/// the built-in sound, so users can use effects they downloaded themselves.
/// </summary>
public sealed class SystemSoundService : ISoundService
{
    private readonly ILogger _logger;
    private readonly string _cacheDirectory;
    private readonly HashSet<string> _reported = [];
    private int _nextAlias;
    private int _stoneVariant;

    // Volume-scaled copies of the stone clicks, pinned because PlaySound reads them asynchronously.
    private readonly Dictionary<(string File, int Volume), GCHandle> _pinned = [];

    public SystemSoundService(ILogger<SystemSoundService>? logger = null, string? dataDirectory = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        string root = dataDirectory ?? AppPaths.DataDirectory;
        CustomDirectory = Path.Combine(root, "sounds");
        _cacheDirectory = Path.Combine(root, "cache", "sounds");
    }

    /// <summary>Where users can drop their own <c>impact_small</c>, <c>explosion_medium</c>, <c>explosion_big</c> files.</summary>
    public string CustomDirectory { get; }

    public static string FileName(SoundEffect effect) => effect switch
    {
        SoundEffect.ImpactSmall => "impact_small",
        SoundEffect.ExplosionMedium => "explosion_medium",
        SoundEffect.Stone => "stone",
        SoundEffect.CaptureSmall => "capture_small",
        SoundEffect.CaptureBig => "capture_big",
        _ => "explosion_big",
    };

    public void Play(SoundEffect effect, double volume)
    {
        volume = Math.Clamp(volume, 0, 1);
        if (volume <= 0)
        {
            return;
        }

        try
        {
            string file = effect == SoundEffect.Stone
                ? ResolveName("stone", $"stone_{(Interlocked.Increment(ref _stoneVariant) % 3) + 1}")
                : Resolve(effect);
            if (OperatingSystem.IsWindows() && effect == SoundEffect.Stone && file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                PlayQuickWindows(file, volume);
            }
            else if (OperatingSystem.IsWindows())
            {
                PlayWindows(file, volume);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Launch("afplay", ["-v", volume.ToString("0.00", CultureInfo.InvariantCulture), file]);
            }
            else if (!Launch("paplay", [$"--volume={(int)(volume * 65536)}", file], quiet: true))
            {
                Launch("aplay", ["-q", file]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            Report(effect.ToString(), $"Could not play {effect}: {ex.Message}");
        }
    }

    /// <summary>The user's replacement file if there is one, otherwise the built-in WAV (copied out of the assembly once).</summary>
    internal string Resolve(SoundEffect effect) =>
        effect == SoundEffect.Stone ? ResolveName("stone", "stone_1") : ResolveName(FileName(effect), FileName(effect));

    internal string ResolveName(string customName, string name)
    {
        foreach (string ext in (string[])[".wav", ".mp3"])
        {
            string custom = Path.Combine(CustomDirectory, customName + ext);
            if (File.Exists(custom))
            {
                return custom;
            }
        }

        string cached = Path.Combine(_cacheDirectory, name + ".wav");
        if (!File.Exists(cached))
        {
            Directory.CreateDirectory(_cacheDirectory);
            using Stream asset = AssetLoader.Open(new Uri($"avares://Hoshi/Assets/Sounds/{name}.wav"));
            string temp = cached + ".tmp";
            using (FileStream output = File.Create(temp))
            {
                asset.CopyTo(output);
            }

            File.Move(temp, cached, overwrite: true);
        }

        return cached;
    }

    /// <summary>
    /// Low-latency path for the short, frequent stone click: PlaySound from memory (a few ms instead of MCI's file
    /// open), with the volume baked into a cached copy of the samples. PlaySound plays one sound at a time, which
    /// is right for clicks; the explosions use MCI and overlap freely.
    /// </summary>
    private void PlayQuickWindows(string file, double volume)
    {
        int level = (int)Math.Round(volume * 20); // 5 % steps keep the cache tiny
        if (!_pinned.TryGetValue((file, level), out GCHandle handle))
        {
            byte[] wav = File.ReadAllBytes(file);
            ScalePcm16(wav, level / 20.0);
            handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
            _pinned[(file, level)] = handle;
        }

        const uint SndAsync = 0x0001;
        const uint SndNoDefault = 0x0002;
        const uint SndMemory = 0x0004;
        if (!NativeMethods.PlaySoundW(handle.AddrOfPinnedObject(), IntPtr.Zero, SndAsync | SndNoDefault | SndMemory))
        {
            Report("playsound", "PlaySound failed for the stone sound");
        }
    }

    /// <summary>Scales the 16-bit PCM samples of a canonical WAV file in place.</summary>
    internal static void ScalePcm16(byte[] wav, double gain)
    {
        // Find the "data" chunk (our WAVs are canonical, but be tolerant of extra chunks).
        int i = 12;
        while (i + 8 <= wav.Length)
        {
            int size = BitConverter.ToInt32(wav, i + 4);
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
            {
                int end = Math.Min(wav.Length, i + 8 + size);
                for (int p = i + 8; p + 1 < end; p += 2)
                {
                    int v = (int)Math.Round(BitConverter.ToInt16(wav, p) * gain);
                    short c = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
                    wav[p] = (byte)(c & 0xFF);
                    wav[p + 1] = (byte)((c >> 8) & 0xFF);
                }

                return;
            }

            i += 8 + size + (size & 1);
        }
    }

    private void PlayWindows(string file, double volume)
    {
        // "mpegvideo" plays both WAV and MP3 and, unlike "waveaudio", supports per-sound volume. Each sound gets its
        // own alias so they can overlap; the alias is closed once the sound has had time to finish.
        string alias = "hoshi" + Interlocked.Increment(ref _nextAlias).ToString(CultureInfo.InvariantCulture);
        Mci($"open \"{file}\" type mpegvideo alias {alias}");
        Mci($"setaudio {alias} volume to {(int)(volume * 1000)}");
        Mci($"play {alias}");
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => Mci($"close {alias}", report: false)), TaskScheduler.Default);
    }

    private void Mci(string command, bool report = true)
    {
        int error = NativeMethods.mciSendStringW(command, null, 0, IntPtr.Zero);
        if (error != 0 && report)
        {
            Report("mci", $"MCI error {error} for '{command.Split(' ')[0]}'");
        }
    }

    private bool Launch(string program, string[] args, bool quiet = false)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = false, RedirectStandardOutput = false };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        try
        {
            using Process? p = Process.Start(info);
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (!quiet)
            {
                Report(program, $"Could not run {program}: {ex.Message}");
            }

            return false;
        }
    }

    private void Report(string key, string message)
    {
        lock (_reported)
        {
            if (!_reported.Add(key))
            {
                return;
            }
        }

        _logger.LogWarning("{Message}", message);
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
#pragma warning disable SYSLIB1054 // LibraryImport would need unsafe code for a single, rarely called function.
        internal static extern int mciSendStringW(string command, System.Text.StringBuilder? returnValue, int returnLength, IntPtr callback);

        [DllImport("winmm.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);
#pragma warning restore SYSLIB1054
    }
}
