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
    /// <summary>Set by platforms with their own audio output (Android's AudioTrack); tried first.</summary>
    public static Func<int, IPcmSink?>? PlatformFactory { get; set; }

    /// <summary>The sink for this OS, or null (with a reason) when there is none.</summary>
    public static IPcmSink? Create(int sampleRate, out string? problem)
    {
        problem = null;
        try
        {
            if (PlatformFactory is { } platform)
            {
                return platform(sampleRate) ?? throw new InvalidOperationException("no audio track");
            }

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

            if (OperatingSystem.IsMacOS())
            {
                return new AudioQueueSink(sampleRate);
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

/// <summary>
/// macOS: an AudioToolbox output queue (part of every macOS since 10.5, nothing to install). A few buffers circulate:
/// <see cref="Write"/> waits for one the queue has finished playing, fills it and enqueues it again.
/// </summary>
internal sealed class AudioQueueSink : IPcmSink
{
    private const int BufferCount = 4;
    private const uint LinearPcm = 0x6C70636D; // 'lpcm'
    private const uint SignedPacked = 0x4 | 0x8; // kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked

    // AudioQueueBuffer (64-bit): UInt32 capacity; void* mAudioData (8); UInt32 mAudioDataByteSize (16); ...
    private const int AudioDataOffset = 8;
    private const int ByteSizeOffset = 16;
    private const int CapacityOffset = 0;

    private readonly IntPtr _queue;
    private readonly NativeMethods.OutputCallback _callback; // kept alive while the queue may call it
    private readonly SemaphoreSlim _free = new(0, BufferCount);
    private readonly System.Collections.Concurrent.ConcurrentQueue<IntPtr> _available = new();
    private readonly List<IntPtr> _buffers = [];
    private short[] _scratch = [];
    private bool _started;
    private int _enqueued;
    private bool _disposed;

    public AudioQueueSink(int sampleRate)
    {
        var format = new AudioStreamBasicDescription
        {
            mSampleRate = sampleRate,
            mFormatID = LinearPcm,
            mFormatFlags = SignedPacked,
            mBytesPerPacket = 4,
            mFramesPerPacket = 1,
            mBytesPerFrame = 4,
            mChannelsPerFrame = 2,
            mBitsPerChannel = 16,
        };
        _callback = OnBufferDone;
        int status = NativeMethods.AudioQueueNewOutput(ref format, _callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue);
        if (status != 0)
        {
            throw new InvalidOperationException($"AudioQueueNewOutput error {status}");
        }
    }

    public void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken)
    {
        int bytes = interleaved.Length * 2;
        IntPtr buffer = Take(bytes, cancellationToken);
        if (_scratch.Length < interleaved.Length)
        {
            _scratch = new short[interleaved.Length];
        }

        interleaved.CopyTo(_scratch);
        Marshal.Copy(_scratch, 0, Marshal.ReadIntPtr(buffer, AudioDataOffset), interleaved.Length);
        Marshal.WriteInt32(buffer, ByteSizeOffset, bytes);
        int status = NativeMethods.AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero);
        if (status != 0)
        {
            throw new InvalidOperationException($"AudioQueueEnqueueBuffer error {status}");
        }

        // Start with two buffers queued, so the first one is not played into silence.
        if (!_started && ++_enqueued >= 2)
        {
            _started = true;
            status = NativeMethods.AudioQueueStart(_queue, IntPtr.Zero);
            if (status != 0)
            {
                throw new InvalidOperationException($"AudioQueueStart error {status}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.AudioQueueStop(_queue, immediate: true);
        NativeMethods.AudioQueueDispose(_queue, immediate: true); // also frees the buffers
        _free.Dispose();
    }

    /// <summary>A free buffer big enough for <paramref name="bytes"/>: a new one while there are fewer than four.</summary>
    private IntPtr Take(int bytes, CancellationToken cancellationToken)
    {
        if (_buffers.Count < BufferCount && _available.IsEmpty)
        {
            int status = NativeMethods.AudioQueueAllocateBuffer(_queue, (uint)Math.Max(bytes, 16_384), out IntPtr fresh);
            if (status != 0)
            {
                throw new InvalidOperationException($"AudioQueueAllocateBuffer error {status}");
            }

            _buffers.Add(fresh);
            return fresh;
        }

        _free.Wait(cancellationToken);
        _available.TryDequeue(out IntPtr buffer);
        if ((uint)Marshal.ReadInt32(buffer, CapacityOffset) < (uint)bytes)
        {
            // The mixer's buffer grew: swap this one for a bigger one.
            NativeMethods.AudioQueueFreeBuffer(_queue, buffer);
            _buffers.Remove(buffer);
            int status = NativeMethods.AudioQueueAllocateBuffer(_queue, (uint)bytes, out buffer);
            if (status != 0)
            {
                throw new InvalidOperationException($"AudioQueueAllocateBuffer error {status}");
            }

            _buffers.Add(buffer);
        }

        return buffer;
    }

    // Called on the queue's own thread when a buffer has been played.
    private void OnBufferDone(IntPtr userData, IntPtr queue, IntPtr buffer)
    {
        if (_disposed)
        {
            return;
        }

        _available.Enqueue(buffer);
        try
        {
            _free.Release();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SemaphoreFullException)
        {
            // Stopping: nothing waits any more.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

#pragma warning disable SYSLIB1054 // A handful of AudioToolbox calls with a callback; DllImport keeps it simple.
    private static class NativeMethods
    {
        private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void OutputCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueNewOutput(ref AudioStreamBasicDescription format, OutputCallback callback, IntPtr userData,
            IntPtr callbackRunLoop, IntPtr callbackRunLoopMode, uint flags, out IntPtr queue);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueAllocateBuffer(IntPtr queue, uint byteSize, out IntPtr buffer);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueFreeBuffer(IntPtr queue, IntPtr buffer);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packetDescriptionCount, IntPtr packetDescriptions);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

        [DllImport(AudioToolbox)]
        public static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);
    }
#pragma warning restore SYSLIB1054
}
