using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Voice notes ----------

    private GameNode? _recordingAt;
    private CancellationTokenSource? _recordingLimit;
    private CancellationTokenSource? _playing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordLabel))]
    private bool _isRecording;

    /// <summary>Where voice notes are kept (set by the main window); null in tests and previews.</summary>
    public VoiceNotes? Voice { get; set; }

    /// <summary>The recorder for voice notes (the phone's microphone; tests set their own).</summary>
    internal IVoiceRecorder? Recorder { get; set; } = PlatformInfo.VoiceRecorder;

    /// <summary>The phone can record a voice note here (the desktop plays them).</summary>
    public bool CanRecord => Voice is not null && Recorder is not null;

    public string RecordLabel => Tr.T(IsRecording ? "Study.RecordStop" : "Study.Record");

    /// <summary>
    /// "Voice note": starts recording on the move on the board; a second tap stops and pins the note there (with the
    /// category and the text typed, if any). A minute at most. Works during your OGS games too, like any note.
    /// </summary>
    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (Voice is null || Recorder is not { } recorder)
        {
            return;
        }

        if (!IsRecording)
        {
            if (!await recorder.StartAsync())
            {
                _game.ShowStatus(Tr.T("Study.RecordRefused"));
                return;
            }

            _recordingAt = _game.CurrentNode;
            IsRecording = true;
            _game.ShowStatus(Tr.T("Study.Recording"));
            _recordingLimit = new CancellationTokenSource();
            _ = StopAtLimit(_recordingLimit.Token);
            return;
        }

        await FinishRecording();
    }

    private async Task StopAtLimit(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(VoiceNotes.MaxLength, cancellationToken);
            if (IsRecording)
            {
                await FinishRecording();
            }
        }
        catch (OperationCanceledException)
        {
            // stopped by hand
        }
    }

    private async Task FinishRecording()
    {
        if (Voice is not { } voice || Recorder is not { } recorder || _recordingAt is not { } node)
        {
            return;
        }

        _recordingLimit?.Cancel();
        IsRecording = false;
        _recordingAt = null;
        short[] samples = await recorder.StopAsync();
        if (samples.Length < recorder.SampleRate / 3)
        {
            _game.ShowStatus(Tr.T("Study.RecordTooShort"));
            return;
        }

        string name;
        try
        {
            name = await Task.Run(() => voice.Save(samples, recorder.SampleRate));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowError(Tr.T("Study.CouldNotSave"), ex.Message);
            return;
        }

        if (!ReferenceEquals(Root(node), _game.Tree.Root))
        {
            voice.Delete(name); // another game is on the board now
            return;
        }

        var pin = new StudyPin(StudyStore.NewId(), NewCategory.Category ?? PinCategory.Idea, AuthorName, _time.GetUtcNow(), NewPinText.Trim(), [], Audio: name);
        NodeStudy study = StudyStore.Read(node);
        StudyStore.Write(node, study with { Pins = [.. study.Pins, pin] });
        NewPinText = string.Empty;
        _game.StudyEdited();
        Sync();
        _game.ShowStatus(Tr.F("Study.Recorded", (int)Math.Round((double)samples.Length / recorder.SampleRate)));
    }

    /// <summary>Plays a pin's voice note; a second tap stops it.</summary>
    internal async Task PlayAsync(StudyPinRow row)
    {
        if (_playing is { } current)
        {
            current.Cancel();
            _playing = null;
            return;
        }

        if (Voice is not { } voice || row.Pin.Audio is not { } name)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _playing = cts;
        try
        {
            if (!await voice.PlayAsync(name, cts.Token))
            {
                _game.ShowStatus(Tr.T(voice.PathOf(name) is null ? "Study.VoiceMissing" : "Study.VoiceNoOutput"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // a file removed meanwhile, or stopped
        }
        finally
        {
            if (ReferenceEquals(_playing, cts))
            {
                _playing = null;
            }
        }
    }
}
