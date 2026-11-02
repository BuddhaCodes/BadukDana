using FluentAssertions;
using Hoshi.App.Services.Backup;
using Hoshi.App.Services.Study;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Sgf.Study;

namespace Hoshi.App.Tests;

/// <summary>Voice notes: recorded on the phone, a WAV next to the games, played everywhere, carried by backups.</summary>
public sealed class VoiceNoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-voice-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (string d in new[] { _dir, _dir + "-other" })
        {
            if (Directory.Exists(d))
            {
                Directory.Delete(d, recursive: true);
            }
        }
    }

    private sealed class FakeRecorder : IVoiceRecorder
    {
        public bool Allow { get; set; } = true;

        public short[] Samples { get; set; } = [.. Enumerable.Range(0, 16000 * 2).Select(i => (short)(Math.Sin(i / 10.0) * 8000))];

        public int SampleRate => 16000;

        public Task<bool> StartAsync() => Task.FromResult(Allow);

        public Task<short[]> StopAsync() => Task.FromResult(Samples);
    }

    [Fact]
    public void A_wav_is_written_and_read_back()
    {
        short[] samples = [0, 1, -1, short.MaxValue, short.MinValue];
        (short[] Samples, int SampleRate)? back = VoiceNotes.ReadWav(VoiceNotes.Wav(samples, 16000));
        back!.Value.SampleRate.Should().Be(16000);
        back.Value.Samples.Should().Equal(samples);
        VoiceNotes.ReadWav("not a wav at all, really not"u8).Should().BeNull();
    }

    [Fact]
    public async Task A_voice_note_is_recorded_on_the_move_and_its_file_goes_with_the_pin()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        var recorder = new FakeRecorder();
        var voice = new VoiceNotes(_dir);
        var study = new StudyViewModel(game) { Voice = voice, Recorder = recorder };
        game.PlayCommand.Execute(new Point(2, 2));
        study.CanRecord.Should().BeTrue();
        study.NewPinText = "Listen";

        await study.ToggleRecordingCommand.ExecuteAsync(null);
        study.IsRecording.Should().BeTrue();
        study.RecordLabel.Should().Be("■ Parar y fijar");
        await study.ToggleRecordingCommand.ExecuteAsync(null);

        study.IsRecording.Should().BeFalse();
        StudyPin pin = StudyStore.Read(game.CurrentNode).Pins.Should().ContainSingle().Which;
        pin.Text.Should().Be("Listen");
        pin.Audio.Should().EndWith(".wav");
        string file = voice.PathOf(pin.Audio)!;
        File.Exists(file).Should().BeTrue();
        game.StatusText.Should().Be("Nota de voz fijada (2 s).");
        StudyPinRow row = study.Pins.Single();
        row.HasAudio.Should().BeTrue();
        row.AudioLabel.Should().Be("▶ 0:02");

        // The SGF keeps only the name.
        Hoshi.Sgf.SgfWriter.Write(game.Tree).Should().Contain(pin.Audio).And.NotContain("RIFF");

        await row.DeleteCommand.ExecuteAsync(null);
        File.Exists(file).Should().BeFalse("the sound goes with its note");
    }

    [Fact]
    public async Task Refused_or_too_short_recordings_keep_nothing()
    {
        var game = new GameViewModel(9, RuleSet.Japanese);
        var recorder = new FakeRecorder { Allow = false };
        var study = new StudyViewModel(game) { Voice = new VoiceNotes(_dir), Recorder = recorder };
        game.PlayCommand.Execute(new Point(2, 2));
        await study.ToggleRecordingCommand.ExecuteAsync(null);
        study.IsRecording.Should().BeFalse();
        game.StatusText.Should().StartWith("El micrófono no está disponible");

        recorder.Allow = true;
        recorder.Samples = new short[1000];
        await study.ToggleRecordingCommand.ExecuteAsync(null);
        await study.ToggleRecordingCommand.ExecuteAsync(null);
        StudyStore.Read(game.CurrentNode).Pins.Should().BeEmpty();
        game.StatusText.Should().Be("Demasiado corta: no se guardó nada.");
        new StudyViewModel(game).CanRecord.Should().BeFalse("the desktop only plays them");
    }

    [Fact]
    public void Voice_notes_travel_in_backups_and_names_from_files_are_never_paths()
    {
        string other = _dir + "-other";
        string name = new VoiceNotes(other).Save(new short[16000], 16000);
        BackupPackage.Merge(other, _dir, applyPreferences: false).NoteCards.Should().Be(1);
        new VoiceNotes(_dir).PathOf(name).Should().NotBeNull();

        StudyStore.IsAudioName("../../secrets/token.wav").Should().BeFalse();
        StudyStore.IsAudioName("a1b2.wav").Should().BeTrue();
        new VoiceNotes(_dir).PathOf("..\\x.wav").Should().BeNull();
    }
}
