using Android.App;
using Android.Content.PM;
using Android.Media;
using Hoshi.App.Services.Study;

namespace Hoshi.Droid;

/// <summary>
/// Voice notes from the phone's microphone: 16 kHz mono PCM read from an AudioRecord on a background thread. The
/// microphone permission is asked the first time a note is recorded (and only then).
/// </summary>
internal sealed class AndroidVoiceRecorder : IVoiceRecorder
{
    public const int PermissionRequest = 4208;
    private const int Rate = 16000;

    private static TaskCompletionSource<bool>? _permission;
    private readonly Func<Activity?> _activity;
    private AudioRecord? _record;
    private Task<short[]>? _reading;
    private volatile bool _stop;

    public AndroidVoiceRecorder(Func<Activity?> activity) => _activity = activity;

    public int SampleRate => Rate;

    public async Task<bool> StartAsync()
    {
        if (_record is not null || _activity() is not { } activity || !await Allowed(activity))
        {
            return false;
        }

        int minimum = AudioRecord.GetMinBufferSize(Rate, ChannelIn.Mono, Encoding.Pcm16bit);
        if (minimum <= 0)
        {
            return false;
        }

        var record = new AudioRecord(AudioSource.Mic, Rate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(minimum, Rate));
        if (record.State != State.Initialized)
        {
            record.Release();
            return false;
        }

        _stop = false;
        record.StartRecording();
        _record = record;
        _reading = Task.Run(() =>
        {
            var all = new List<short>(Rate * 10);
            var buffer = new short[Rate / 10];
            while (!_stop && all.Count < Rate * (int)VoiceNotes.MaxLength.TotalSeconds)
            {
                int n = record.Read(buffer, 0, buffer.Length);
                if (n < 0)
                {
                    break;
                }

                all.AddRange(buffer.AsSpan(0, n));
            }

            return all.ToArray();
        });
        return true;
    }

    public async Task<short[]> StopAsync()
    {
        if (_record is not { } record || _reading is not { } reading)
        {
            return [];
        }

        _stop = true;
        short[] samples = await reading;
        try
        {
            record.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
            // already stopped
        }

        record.Release();
        _record = null;
        _reading = null;
        return samples;
    }

    /// <summary>MainActivity passes on the answer to the microphone question.</summary>
    public static void OnPermissionResult(int requestCode, Permission[] results)
    {
        if (requestCode == PermissionRequest)
        {
            TaskCompletionSource<bool>? pending = _permission;
            _permission = null;
            pending?.TrySetResult(results.Length > 0 && results[0] == Permission.Granted);
        }
    }

    private static Task<bool> Allowed(Activity activity)
    {
        if (activity.CheckSelfPermission(Android.Manifest.Permission.RecordAudio) == Permission.Granted)
        {
            return Task.FromResult(true);
        }

        _permission?.TrySetResult(false);
        var ask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _permission = ask;
        activity.RunOnUiThread(() => activity.RequestPermissions([Android.Manifest.Permission.RecordAudio], PermissionRequest));
        return ask.Task;
    }
}
