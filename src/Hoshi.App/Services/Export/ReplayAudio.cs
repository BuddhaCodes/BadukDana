using System.Buffers.Binary;

namespace Hoshi.App.Services.Export;

/// <summary>16-bit stereo PCM at 44.1 kHz, as in Hoshi's own sound files.</summary>
public sealed record PcmClip(short[] Samples)
{
    public const int Rate = 44100;

    public const int Channels = 2;

    /// <summary>Reads a canonical 16-bit WAV (mono is spread to both channels); null when it is another format.</summary>
    public static PcmClip? FromWav(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[8] != 'W')
        {
            return null;
        }

        int channels = 0, bits = 0, rate = 0, pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 4));
            int body = pos + 8;
            if (id == "fmt ")
            {
                channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 14));
            }
            else if (id == "data")
            {
                if (bits != 16 || rate != Rate || channels is not (1 or 2))
                {
                    return null;
                }

                int count = Math.Min(length, wav.Length - body) / 2;
                var raw = new short[count];
                for (int i = 0; i < count; i++)
                {
                    raw[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + (i * 2)));
                }

                if (channels == 2)
                {
                    return new PcmClip(raw);
                }

                var stereo = new short[count * 2];
                for (int i = 0; i < count; i++)
                {
                    stereo[2 * i] = stereo[(2 * i) + 1] = raw[i];
                }

                return new PcmClip(stereo);
            }

            pos = body + length + (length & 1);
        }

        return null;
    }
}

/// <summary>The replay's sound track: each step's sound placed at the time its picture appears.</summary>
public static class ReplayAudio
{
    /// <summary>A WAV file (16-bit stereo, 44.1 kHz) as long as the replay.</summary>
    public static byte[] Track(IReadOnlyList<ReplayStep> steps, IReadOnlyDictionary<ReplaySound, PcmClip> clips, double volume = 0.8)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(clips);
        long frames = (long)Math.Ceiling(ReplayPlan.Length(steps).TotalSeconds * PcmClip.Rate);
        var mix = new int[frames * PcmClip.Channels];
        TimeSpan at = TimeSpan.Zero;
        foreach (ReplayStep step in steps)
        {
            if (clips.TryGetValue(step.Sound, out PcmClip? clip))
            {
                long start = (long)(at.TotalSeconds * PcmClip.Rate) * PcmClip.Channels;
                for (int i = 0; i < clip.Samples.Length && start + i < mix.Length; i++)
                {
                    mix[start + i] += (int)(clip.Samples[i] * volume);
                }
            }

            at += step.Duration;
        }

        byte[] wav = new byte[44 + (mix.Length * 2)];
        void Ascii(int offset, string text) => System.Text.Encoding.ASCII.GetBytes(text, 0, 4, wav, offset);
        Ascii(0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), PcmClip.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), PcmClip.Rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), PcmClip.Rate * PcmClip.Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), PcmClip.Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        Ascii(36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), mix.Length * 2);
        for (int i = 0; i < mix.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + (i * 2)), (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue));
        }

        return wav;
    }
}
