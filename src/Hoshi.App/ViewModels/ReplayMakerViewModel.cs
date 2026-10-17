using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Export;
using Hoshi.App.Services.Study;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>A choice of shape in the replay maker.</summary>
public sealed record ReplayFormatOption(ReplayFormat Format, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The replay maker (desktop): a video of the line through the shown move for social networks, with a title card,
/// the big moments and the study notes, as MP4 (through FFmpeg), GIF or an animated web page. Previewed step by
/// step before saving; everything is made on this computer.
/// </summary>
public sealed partial class ReplayMakerViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly IFileDialogService? _files;
    private readonly Func<IReadOnlyDictionary<GameNode, StudyVerdict>>? _verdicts;
    private readonly Func<ShareOptions> _share;
    private readonly Func<bool> _coordinates;
    private readonly IVideoEncoder _video;
    private CancellationTokenSource? _work;
    private IReadOnlyList<ReplayStep> _steps = [];

    [ObservableProperty]
    private int _fromMove = 1;

    [ObservableProperty]
    private int _toMove = 1;

    [ObservableProperty]
    private double _secondsPerMove = 0.8;

    [ObservableProperty]
    private ReplayFormatOption _selectedFormat;

    [ObservableProperty]
    private bool _intro = true;

    [ObservableProperty]
    private bool _notes = true;

    [ObservableProperty]
    private bool _highlights = true;

    [ObservableProperty]
    private bool _sounds = true;

    /// <summary>The critical moves move: the app's impact for strong ones, a red shock for mistakes.</summary>
    [ObservableProperty]
    private bool _effects = true;

    [ObservableProperty]
    private int _previewIndex;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportMp4Command), nameof(ExportGifCommand), nameof(ExportWebCommand), nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = string.Empty;

    public ReplayMakerViewModel(
        GameViewModel game,
        IFileDialogService? files = null,
        Func<IReadOnlyDictionary<GameNode, StudyVerdict>>? verdicts = null,
        Func<ShareOptions>? share = null,
        Func<bool>? coordinates = null,
        IVideoEncoder? video = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _files = files;
        _verdicts = verdicts;
        _share = share ?? (() => new ShareOptions());
        _coordinates = coordinates ?? (() => true);
        _video = video ?? new FfmpegVideoEncoder();
        Formats =
        [
            new(ReplayFormat.Wide, Tr.T("Replay.Wide")),
            new(ReplayFormat.Square, Tr.T("Replay.Square")),
            new(ReplayFormat.Vertical, Tr.T("Replay.Vertical")),
        ];
        _selectedFormat = Formats[0];
    }

    public IReadOnlyList<ReplayFormatOption> Formats { get; }

    /// <summary>The moves of the line through the shown move.</summary>
    public int MaxMove { get; private set; } = 1;

    public IReadOnlyList<ReplayStep> Steps => _steps;

    public int LastStep => Math.Max(0, _steps.Count - 1);

    public string LengthText => Tr.F("Replay.Length", _steps.Count, ReplayPlan.Length(_steps).ToString(@"m\:ss", CultureInfo.InvariantCulture));

    public bool CanMakeVideo => _video.IsAvailable;

    public string? VideoHint => CanMakeVideo ? null : Tr.T("Replay.NoFfmpeg");

    /// <summary>The number boxes (decimal, as the controls want).</summary>
    public decimal? FromBox
    {
        get => FromMove;
        set => FromMove = (int)Math.Clamp(NumberBox.Whole(value, 1, MaxMove), 1, MaxMove);
    }

    public decimal? ToBox
    {
        get => ToMove;
        set => ToMove = (int)Math.Clamp(NumberBox.Whole(value, 1, MaxMove), 1, MaxMove);
    }

    public ReplayOptions Options => new()
    {
        FromMove = FromMove,
        ToMove = ToMove,
        SecondsPerMove = SecondsPerMove,
        Format = SelectedFormat.Format,
        Intro = Intro,
        Notes = Notes,
        Highlights = Highlights,
        Sounds = Sounds,
        Effects = Effects,
        Coordinates = _coordinates(),
        Share = _share(),
    };

    /// <summary>Starts with the whole line through the shown move (call when the window opens).</summary>
    public void Reset()
    {
        GameNode end = _game.CurrentNode;
        while (end.Children.Count > 0)
        {
            end = end.Children[0];
        }

        MaxMove = Math.Max(1, GameCursor.Path(end).Count(n => n.HasMove));
        OnPropertyChanged(nameof(MaxMove));
        _resetting = true;
        try
        {
            FromMove = 1;
            ToMove = MaxMove;
        }
        finally
        {
            _resetting = false;
        }

        OnPropertyChanged(nameof(CanMakeVideo));
        OnPropertyChanged(nameof(VideoHint));
        Status = string.Empty;
        Rebuild();
    }

    /// <summary>The verdicts used for the highlights: the written review, else the tree's colours.</summary>
    public IReadOnlyDictionary<GameNode, StudyVerdict> Verdicts() => _verdicts?.Invoke() ?? new Dictionary<GameNode, StudyVerdict>();

    partial void OnFromMoveChanged(int value)
    {
        if (ToMove < value)
        {
            ToMove = value;
        }

        OnPropertyChanged(nameof(FromBox));
        Rebuild();
    }

    partial void OnToMoveChanged(int value)
    {
        if (FromMove > value)
        {
            FromMove = value;
        }

        OnPropertyChanged(nameof(ToBox));
        Rebuild();
    }

    partial void OnSecondsPerMoveChanged(double value) => Rebuild();

    partial void OnSelectedFormatChanged(ReplayFormatOption value) => Rebuild();

    partial void OnIntroChanged(bool value) => Rebuild();

    partial void OnNotesChanged(bool value) => Rebuild();

    partial void OnHighlightsChanged(bool value) => Rebuild();

    partial void OnSoundsChanged(bool value) => Rebuild();

    partial void OnEffectsChanged(bool value) => Rebuild();

    /// <summary>The step shown is a big moment with an effect.</summary>
    public bool PreviewHasEffect => Effects && _steps.Count > 0 && _steps[Math.Clamp(PreviewIndex, 0, LastStep)].EffectSeconds > 0;

    /// <summary>The big moments of the replay (good and bad), for the summary under the preview.</summary>
    public int MomentCount => _steps.Count(s => s.Moment != ReplayMoment.None && s.Kind == ReplayStepKind.Move);

    /// <summary>How many big moments were found, or what is needed to find them.</summary>
    public string MomentsText => MomentCount > 0
        ? Tr.F("Replay.Moments", _steps.Count(s => s.IsGood && s.Kind == ReplayStepKind.Move), _steps.Count(s => s.Moment is ReplayMoment.Mistake or ReplayMoment.Blunder && s.Kind == ReplayStepKind.Move))
        : Tr.T("Replay.EffectsHint");

    /// <summary>Frames per second of a moment's effect: the video's rate, slower for GIFs (size).</summary>
    public const double GifEffectFps = 12.5;

    partial void OnPreviewIndexChanged(int value) => RenderPreview();

    private bool _resetting;

    private void Rebuild()
    {
        if (_resetting)
        {
            return;
        }

        _steps = ReplayPlan.Build(_game.Tree, _game.CurrentNode, Options, Verdicts());
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(LastStep));
        OnPropertyChanged(nameof(LengthText));
        OnPropertyChanged(nameof(MomentCount));
        OnPropertyChanged(nameof(MomentsText));
        if (PreviewIndex > LastStep)
        {
            PreviewIndex = LastStep;
        }
        else
        {
            RenderPreview();
        }
    }

    private void RenderPreview()
    {
        if (_steps.Count == 0 || Avalonia.Application.Current is null)
        {
            Preview = null;
            return;
        }

        // A big moment is previewed at the height of its effect.
        ReplayStep step = _steps[Math.Clamp(PreviewIndex, 0, LastStep)];
        double? peak = Effects && step.EffectSeconds > 0 ? (step.IsGood ? 0.28 : 0.12) : null;
        Preview = ReplayScene.Render(step, Players(), SelectedFormat.Format, _coordinates(), small: true, effectSeconds: peak);
        OnPropertyChanged(nameof(PreviewHasEffect));
    }

    private ReplayPlayers Players()
    {
        ShareOptions share = _share();
        return new ReplayPlayers(share.Black(_game.Tree), share.White(_game.Tree), share.ShowBrand);
    }

    private bool CanExport() => !IsBusy && _steps.Count > 0;

    private bool CanExportVideo() => CanExport() && CanMakeVideo;

    [RelayCommand(CanExecute = nameof(CanExportVideo))]
    private Task ExportMp4() => Export(ExportFileType.Mp4, async (path, ct, progress) =>
    {
        PixelSizeInfo size = SizeOf(small: false);
        byte[]? wav = Sounds ? ReplayAudio.Track(_steps, LoadClips()) : null;
        await _video.EncodeAsync(path, size.Width, size.Height, Frames(small: false, ct), wav, progress, ReplayPlan.Length(_steps), ct);
    });

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportGif() => Export(ExportFileType.Gif, async (path, ct, progress) =>
    {
        PixelSizeInfo size = SizeOf(small: true);
        var frames = new List<GifFrame>();
        TimeSpan total = ReplayPlan.Length(_steps), done = TimeSpan.Zero;
        await foreach ((byte[] bgra, TimeSpan duration) in Frames(small: true, ct))
        {
            frames.Add(new GifFrame(bgra, duration));
            done += duration;
            progress.Report(0.8 * done.TotalSeconds / Math.Max(0.001, total.TotalSeconds));
        }

        byte[] gif = await Task.Run(() => GifEncoder.Encode(size.Width, size.Height, frames), ct);
        await File.WriteAllBytesAsync(path, gif, ct);
    });

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportWeb() => Export(ExportFileType.Html, async (path, ct, _) =>
    {
        var play = _steps.Where(s => s.Node is not null).Select(s => (s.Node!, s.Duration, s.Highlight, Effects ? s.Moment : ReplayMoment.None)).ToList();
        string html = WebViewer.Html(_game.Tree, _game.CurrentNode, _share(), play);
        await File.WriteAllTextAsync(path, html, new System.Text.UTF8Encoding(false), ct);
    });

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _work?.Cancel();

    /// <summary>
    /// The frames of the replay in order, rendered on the UI thread one at a time. A big moment with effects is
    /// animated (30 frames a second for the video, 12.5 for a GIF) and then held still for the rest of its time; the
    /// durations add up to the step's, so the sound stays in step.
    /// </summary>
    public async IAsyncEnumerable<(byte[] Bgra, TimeSpan Duration)> Frames(bool small, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReplayPlayers players = Players();
        bool coordinates = _coordinates();
        double fps = small ? GifEffectFps : FfmpegVideoEncoder.FramesPerSecond;
        foreach (ReplayStep step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int animated = Effects ? (int)Math.Floor(step.EffectSeconds * fps) : 0;
            TimeSpan frame = TimeSpan.FromSeconds(1 / fps);
            for (int i = 0; i < animated; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] moving = BoardPicture.Bgra(ReplayScene.Render(step, players, SelectedFormat.Format, coordinates, small, effectSeconds: i / fps));
                yield return (moving, frame);
                await Task.Yield();
            }

            TimeSpan rest = step.Duration - (frame * animated);
            if (rest > TimeSpan.Zero || animated == 0)
            {
                byte[] bgra = BoardPicture.Bgra(ReplayScene.Render(step, players, SelectedFormat.Format, coordinates, small));
                yield return (bgra, animated == 0 ? step.Duration : rest);
                await Task.Yield();
            }
        }
    }

    private readonly record struct PixelSizeInfo(int Width, int Height);

    private PixelSizeInfo SizeOf(bool small)
    {
        Avalonia.PixelSize full = ReplayScene.Size(SelectedFormat.Format);
        return small ? new PixelSizeInfo(full.Width / 2, full.Height / 2) : new PixelSizeInfo(full.Width, full.Height);
    }

    private static Dictionary<ReplaySound, PcmClip> LoadClips()
    {
        var clips = new Dictionary<ReplaySound, PcmClip>();
        foreach ((ReplaySound sound, string name) in new[]
        {
            (ReplaySound.Stone, "stone_1"), (ReplaySound.Capture, "capture_small"),
            (ReplaySound.Strong, "explosion_medium"), (ReplaySound.Brilliant, "explosion_big"),
            (ReplaySound.Mistake, "impact_small"), (ReplaySound.Blunder, "capture_big"),
        })
        {
            try
            {
                using Stream stream = AssetLoader.Open(new Uri($"avares://Hoshi.App/Assets/Sounds/{name}.wav"));
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                if (PcmClip.FromWav(memory.ToArray()) is { } clip)
                {
                    clips[sound] = clip;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
            {
                // No sound for that step.
            }
        }

        return clips;
    }

    private async Task Export(ExportFileType type, Func<string, CancellationToken, IProgress<double>, Task> make)
    {
        string name = $"{_share().Black(_game.Tree)} vs {_share().White(_game.Tree)} — {Tr.T("Replay.Suffix")}.{type.Extension}";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        if (_files is null || await _files.PickExportToSaveAsync(name, type) is not { } path)
        {
            return;
        }

        bool existed = File.Exists(path);
        IsBusy = true;
        Progress = 0;
        _work = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            Progress = p;
            Status = Tr.F("Replay.Working", p);
        });
        try
        {
            Status = Tr.F("Replay.Working", 0.0);
            await make(path, _work.Token, progress);
            await _files.FinishSaveAsync(path);
            Progress = 1;
            Status = Tr.F("Replay.Saved", Path.GetFileName(path));
            _game.ShowStatus(Status);
        }
        catch (OperationCanceledException)
        {
            Status = Tr.T("Replay.Cancelled");
            if (!existed)
            {
                TryDelete(path); // only a half-written file Hoshi made
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Status = Tr.F("Replay.Failed", ex.Message);
            if (!existed)
            {
                TryDelete(path);
            }
        }
        finally
        {
            IsBusy = false;
            _work.Dispose();
            _work = null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as it was.
        }
    }
}
