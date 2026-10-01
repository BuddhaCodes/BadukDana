using System.Diagnostics;
using System.Runtime.InteropServices;
using Hoshi.Core.Localization;

namespace Hoshi.App.Services.Music;

/// <summary>A continuous 16-bit stereo PCM output. <see cref="Write"/> blocks until the device can take more.</summary>
public interface IPcmSink : IDisposable
{
    void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken);
}

public static class PcmSinks
{
    /// <summary>The sink for this OS, or null (with a reason) when there is none.</summary>
    public static IPcmSink? Create(int sampleRate, out string? problem)
    {
        problem = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return new WaveOutSink(sampleRate);
            }

            if (OperatingSystem.IsLinux())
            {
                return PipeSink.TryStart("pacat", ["--raw", "--format=s16le", $"--rate={sampleRate}", "--channels=2", "--latency-msec=120"])
                    ?? PipeSink.TryStart("aplay", ["-q", "-t", "raw", "-f", "S16_LE", "-r", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c", "2"])
                    ?? throw new InvalidOperationException("no pacat or aplay");
            }

            problem = Tr.T("Music.NotOnMac");
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            problem = Tr.F("Music.NoAudioOutput", ex.Message);
            return null;
        }
    }
}

/// <summary>Windows waveOut (winmm) with a small ring of buffers, polled for completion.</summary>
internal sealed class WaveOutSink : IPcmSink
{
    private const int BufferCount = 4;
    private const int WhdrDone = 0x1;
    private const int WaveMapper = -1;

    private readonly IntPtr _device;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _data = new IntPtr[BufferCount];
    private readonly int[] _capacity = new int[BufferCount];
    private readonly bool[] _queued = new bool[BufferCount];
    private int _next;
    private short[] _scratch = [];

    public WaveOutSink(int sampleRate)
    {
        var format = new WaveFormatEx
        {
            wFormatTag = 1, // PCM
            nChannels = 2,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 4,
            nAvgBytesPerSec = (uint)sampleRate * 4,
            cbSize = 0,
        };
        int result = NativeMethods.waveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            throw new InvalidOperationException($"waveOutOpen error {result}");
        }

        for (int i = 0; i < BufferCount; i++)
        {
            _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        }
    }

    public void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken)
    {
        int slot = _next;
        // Wait until this slot's previous buffer has been played.
        while (_queued[slot] && (Marshal.ReadInt32(_headers[slot], FlagsOffset) & WhdrDone) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(5);
        }

        int size = Marshal.SizeOf<WaveHeader>();
        if (_queued[slot])
        {
            NativeMethods.waveOutUnprepareHeader(_device, _headers[slot], (uint)size);
        }

        int bytes = interleaved.Length * 2;
        if (_capacity[slot] < bytes)
        {
            if (_data[slot] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_data[slot]);
            }

            _data[slot] = Marshal.AllocHGlobal(bytes);
            _capacity[slot] = bytes;
        }

        if (_scratch.Length < interleaved.Length)
        {
            _scratch = new short[interleaved.Length];
        }

        interleaved.CopyTo(_scratch);
        Marshal.Copy(_scratch, 0, _data[slot], interleaved.Length);

        var header = new WaveHeader { lpData = _data[slot], dwBufferLength = (uint)bytes };
        Marshal.StructureToPtr(header, _headers[slot], false);
        NativeMethods.waveOutPrepareHeader(_device, _headers[slot], (uint)size);
        NativeMethods.waveOutWrite(_device, _headers[slot], (uint)size);
        _queued[slot] = true;
        _next = (slot + 1) % BufferCount;
    }

    public void Dispose()
    {
        NativeMethods.waveOutReset(_device);
        int size = Marshal.SizeOf<WaveHeader>();
        for (int i = 0; i < BufferCount; i++)
        {
            if (_queued[i])
            {
                NativeMethods.waveOutUnprepareHeader(_device, _headers[i], (uint)size);
            }

            Marshal.FreeHGlobal(_headers[i]);
            if (_data[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_data[i]);
            }
        }

        NativeMethods.waveOutClose(_device);
    }

    private static readonly int FlagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.dwFlags));

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

#pragma warning disable SYSLIB1054 // A handful of rarely called winmm functions; DllImport keeps it simple.
    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        internal static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceId, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        internal static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        internal static extern int waveOutClose(IntPtr hWaveOut);
    }
#pragma warning restore SYSLIB1054
}

/// <summary>Linux: raw PCM piped into pacat/aplay, whose blocking stdin paces us.</summary>
internal sealed class PipeSink : IPcmSink
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private byte[] _bytes = [];

    private PipeSink(Process process)
    {
        _process = process;
        _stdin = process.StandardInput.BaseStream;
    }

    public static PipeSink? TryStart(string program, string[] args)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        try
        {
            return Process.Start(info) is { } p ? new PipeSink(p) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken)
    {
        int n = interleaved.Length * 2;
        if (_bytes.Length < n)
        {
            _bytes = new byte[n];
        }

        MemoryMarshal.AsBytes(interleaved).CopyTo(_bytes);
        _stdin.Write(_bytes, 0, n);
    }

    public void Dispose()
    {
        try
        {
            _stdin.Dispose();
            if (!_process.WaitForExit(500))
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }
}
