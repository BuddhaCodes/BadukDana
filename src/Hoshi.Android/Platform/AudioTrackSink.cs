using Android.Media;
using Hoshi.App.Services.Music;

namespace Hoshi.Droid;

/// <summary>The music's 16-bit stereo PCM, streamed to an AudioTrack (Write blocks until the track takes it).</summary>
internal sealed class AudioTrackSink : IPcmSink
{
    private readonly AudioTrack _track;

    public AudioTrackSink(int sampleRate)
    {
        int minimum = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Stereo, Encoding.Pcm16bit);
        _track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Game)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)!
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.Pcm16bit)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)!
            .SetBufferSizeInBytes(Math.Max(minimum, sampleRate))! // about a quarter of a second
            .SetTransferMode(AudioTrackMode.Stream)!
            .Build()!;
        _track.Play();
    }

    public void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken)
    {
        short[] samples = interleaved.ToArray();
        int offset = 0;
        while (offset < samples.Length && !cancellationToken.IsCancellationRequested)
        {
            int written = _track.Write(samples, offset, samples.Length - offset);
            if (written <= 0)
            {
                return;
            }

            offset += written;
        }
    }

    public void Dispose()
    {
        try
        {
            _track.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
            // Never started.
        }

        _track.Release();
    }
}
