using System.Diagnostics;
using System.Globalization;

namespace Hoshi.App.Services.Export;

/// <summary>Writes a video from BGRA frames (each shown for a while) and an optional WAV track.</summary>
public interface IVideoEncoder
{
    /// <summary>Whether this encoder can work here (FFmpeg found).</summary>
    bool IsAvailable { get; }

    Task EncodeAsync(string path, int width, int height, IAsyncEnumerable<(byte[] Bgra, TimeSpan Duration)> frames, byte[]? wav, IProgress<double>? progress, TimeSpan total, CancellationToken cancellationToken);
}

/// <summary>
/// MP4 (H.264 + AAC) through FFmpeg, which the player installs (Hoshi does not bundle it: ffmpeg.org). Frames go to
/// FFmpeg's input as raw BGRA at 30 fps; nothing leaves the computer.
/// </summary>
public sealed class FfmpegVideoEncoder(Func<string?>? configuredPath = null) : IVideoEncoder
{
    public const int FramesPerSecond = 30;

    public bool IsAvailable => Find() is not null;

    /// <summary>The FFmpeg to use: the one chosen in Preferences, else <c>ffmpeg</c> on the PATH.</summary>
    public string? Find()
    {
        if (configuredPath?.Invoke() is { Length: > 0 } chosen && File.Exists(chosen))
        {
            return chosen;
        }

        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        foreach (string known in OperatingSystem.IsMacOS() ? ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"] : Array.Empty<string>())
        {
            if (File.Exists(known))
            {
                return known;
            }
        }

        return null;
    }

    public async Task EncodeAsync(string path, int width, int height, IAsyncEnumerable<(byte[] Bgra, TimeSpan Duration)> frames, byte[]? wav, IProgress<double>? progress, TimeSpan total, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        string ffmpeg = Find() ?? throw new InvalidOperationException("FFmpeg was not found.");
        string? audio = null;
        if (wav is not null)
        {
            audio = Path.Combine(Path.GetTempPath(), $"hoshi-replay-{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(audio, wav, cancellationToken);
        }

        try
        {
            var start = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in Arguments(path, width, height, audio))
            {
                start.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg did not start.");
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
            Stream input = process.StandardInput.BaseStream;
            long written = 0;
            long totalFrames = Math.Max(1, (long)Math.Round(total.TotalSeconds * FramesPerSecond));
            double clock = 0;
            try
            {
                await foreach ((byte[] bgra, TimeSpan duration) in frames.WithCancellation(cancellationToken))
                {
                    // Frames are counted on a running clock so long replays do not drift.
                    clock += duration.TotalSeconds * FramesPerSecond;
                    long until = Math.Max(written + 1, (long)Math.Round(clock));
                    for (; written < until; written++)
                    {
                        await input.WriteAsync(bgra, cancellationToken);
                    }

                    progress?.Report(Math.Min(1, (double)written / totalFrames));
                }

                await input.FlushAsync(cancellationToken);
            }
            finally
            {
                input.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                string log = await errors;
                throw new IOException("FFmpeg: " + string.Join(" ", log.Split('\n').TakeLast(3)).Trim());
            }
        }
        finally
        {
            if (audio is not null)
            {
                try
                {
                    File.Delete(audio);
                }
                catch (IOException)
                {
                    // A temp file left behind.
                }
            }
        }
    }

    /// <summary>FFmpeg's command line: raw frames on its input, the WAV, H.264/AAC for every player and site.</summary>
    public static IReadOnlyList<string> Arguments(string path, int width, int height, string? audio)
    {
        var args = new List<string>
        {
            "-y", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"),
            "-r", FramesPerSecond.ToString(CultureInfo.InvariantCulture), "-i", "-",
        };
        if (audio is not null)
        {
            args.AddRange(["-i", audio]);
        }

        args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart"]);
        if (audio is not null)
        {
            args.AddRange(["-c:a", "aac", "-b:a", "128k", "-shortest"]);
        }

        args.Add(path);
        return args;
    }
}
