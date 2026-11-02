using System.Buffers.Binary;
using Hoshi.App.Services.Music;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Services.Study;

/// <summary>Records voice notes (set by Android: the phone's microphone).</summary>
public interface IVoiceRecorder
{
    /// <summary>Samples per second of what <see cref="StopAsync"/> returns (16-bit mono).</summary>
    int SampleRate { get; }

    /// <summary>Asks for the microphone when needed and starts; false when it is refused or busy.</summary>
    Task<bool> StartAsync();

    /// <summary>Stops and returns what was recorded (empty when nothing was).</summary>
    Task<short[]> StopAsync();
}

/// <summary>
/// Voice notes: each is a small WAV file (16-bit mono) in <c>study/audio</c> of Hoshi's data folder, named after a
/// new id; the pin keeps only that name (<see cref="StudyPin.Audio"/>). The folder travels in backups and folder
/// sync. Plays through the same audio output as the music, on every platform.
/// </summary>
public sealed class VoiceNotes
{
    /// <summary>A note is at most a minute: a thought, not a lecture (and about 2 MB at 16 kHz).</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromSeconds(60);

    public VoiceNotes(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        Directory = Path.Combine(dataDirectory, "study", "audio");
    }

    public string Directory { get; }

    /// <summary>The file of a voice note, or null when the name is not one Hoshi wrote or the file is missing.</summary>
    public string? PathOf(string? name) =>
        StudyStore.IsAudioName(name) && File.Exists(Path.Combine(Directory, name!)) ? Path.Combine(Directory, name!) : null;

    /// <summary>Saves the samples as a new note and returns its name.</summary>
    public string Save(ReadOnlySpan<short> samples, int sampleRate)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string name = StudyStore.NewId() + ".wav";
        File.WriteAllBytes(Path.Combine(Directory, name), Wav(samples, sampleRate));
        return name;
    }

    public void Delete(string? name)
    {
        if (PathOf(name) is { } path)
        {
            File.Delete(path);
        }
    }

    /// <summary>The length of a note.</summary>
    public TimeSpan? Length(string? name)
    {
        if (PathOf(name) is not { } path || ReadWav(File.ReadAllBytes(path)) is not { } wav)
        {
            return null;
        }

        return TimeSpan.FromSeconds((double)wav.Samples.Length / wav.SampleRate);
    }

    /// <summary>Plays a note to the end (or until cancelled) on the audio output; false when there is none.</summary>
    public async Task<bool> PlayAsync(string name, CancellationToken cancellationToken)
    {
        if (PathOf(name) is not { } path || ReadWav(await File.ReadAllBytesAsync(path, cancellationToken)) is not { } wav)
        {
            return false;
        }

        return await Task.Run(
            () =>
            {
                using IPcmSink? sink = PcmSinks.Create(wav.SampleRate, out _);
                if (sink is null)
                {
                    return false;
                }

                // The sinks take stereo: each sample twice, a tenth of a second at a time.
                int chunk = wav.SampleRate / 10;
                var stereo = new short[chunk * 2];
                for (int start = 0; start < wav.Samples.Length && !cancellationToken.IsCancellationRequested; start += chunk)
                {
                    int n = Math.Min(chunk, wav.Samples.Length - start);
                    for (int i = 0; i < n; i++)
                    {
                        stereo[2 * i] = stereo[(2 * i) + 1] = wav.Samples[start + i];
                    }

                    sink.Write(stereo.AsSpan(0, n * 2), cancellationToken);
                }

                return true;
            },
            CancellationToken.None);
    }

    /// <summary>A PCM WAV file (RIFF, 16-bit mono).</summary>
    public static byte[] Wav(ReadOnlySpan<short> samples, int sampleRate)
    {
        var bytes = new byte[44 + (samples.Length * 2)];
        Span<byte> b = bytes;
        "RIFF"u8.CopyTo(b);
        BinaryPrimitives.WriteInt32LittleEndian(b[4..], bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(b[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(b[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(b[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(b[22..], 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(b[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(b[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(b[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(b[34..], 16);
        "data"u8.CopyTo(b[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(b[40..], samples.Length * 2);
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(b[(44 + (2 * i))..], samples[i]);
        }

        return bytes;
    }

    /// <summary>Reads a 16-bit PCM WAV (mono, or the first channel of stereo); null for anything else.</summary>
    public static (short[] Samples, int SampleRate)? ReadWav(ReadOnlySpan<byte> b)
    {
        if (b.Length < 44 || !b[..4].SequenceEqual("RIFF"u8) || !b[8..12].SequenceEqual("WAVE"u8))
        {
            return null;
        }

        int channels = 0, rate = 0, bits = 0;
        int at = 12;
        while (at + 8 <= b.Length)
        {
            ReadOnlySpan<byte> id = b.Slice(at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(b[(at + 4)..]);
            int body = at + 8;
            if (size < 0 || body + size > b.Length)
            {
                size = b.Length - body; // a file cut short: read what is there
            }

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                if (BinaryPrimitives.ReadInt16LittleEndian(b[body..]) != 1)
                {
                    return null; // not PCM
                }

                channels = BinaryPrimitives.ReadInt16LittleEndian(b[(body + 2)..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(b[(body + 4)..]);
                bits = BinaryPrimitives.ReadInt16LittleEndian(b[(body + 14)..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (bits != 16 || channels is < 1 or > 2 || rate is < 4000 or > 96000)
                {
                    return null;
                }

                int frames = size / (2 * channels);
                var samples = new short[frames];
                for (int i = 0; i < frames; i++)
                {
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(b[(body + (i * 2 * channels))..]);
                }

                return (samples, rate);
            }

            at = body + size + (size & 1);
        }

        return null;
    }
}
