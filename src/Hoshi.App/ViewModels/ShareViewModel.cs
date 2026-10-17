using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Export;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Export;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Everything that leaves the computer, always on the user's click: the board as a PNG, the game (or the line through
/// the shown move, to its end) as an animated GIF, text diagrams and the SGF for the clipboard, and pasting a game.
/// Nothing is sent anywhere by Hoshi; files go where the user saves them, or to the app picked in Android's share sheet.
/// </summary>
public sealed partial class ShareViewModel : ViewModelBase
{
    /// <summary>Size of exported boards, in pixels.</summary>
    public const int ImagePixels = 800;

    /// <summary>Size of animation frames: smaller, so a whole game stays a few megabytes.</summary>
    public const int AnimationPixels = 480;

    public static readonly TimeSpan MoveDelay = TimeSpan.FromMilliseconds(550);
    public static readonly TimeSpan BigMoveDelay = TimeSpan.FromMilliseconds(1300);
    public static readonly TimeSpan LastFrameDelay = TimeSpan.FromSeconds(3);

    private readonly GameViewModel _game;
    private readonly IFileDialogService? _files;
    private readonly Func<IReadOnlyDictionary<GameNode, MoveQuality>>? _verdicts;
    private readonly Func<bool> _coordinates;
    private readonly IClipboardService? _clipboard;
    private readonly Func<string, Task>? _openLink;
    private readonly ISettingsService? _settings;

    /// <summary>Shared pictures and pages say "Black"/"White" instead of the names (and leave out the date).</summary>
    [ObservableProperty]
    private bool _hideNames;

    /// <summary>"Made with Hoshi" on shared pictures and pages.</summary>
    [ObservableProperty]
    private bool _showBrand = true;

    public ShareOptions Options => new(HideNames, ShowBrand);

    partial void OnHideNamesChanged(bool value) => _settings?.Save(_settings.Current with { ShareHideNames = value });

    partial void OnShowBrandChanged(bool value) => _settings?.Save(_settings.Current with { ShareShowBrand = value });

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportImageCommand), nameof(ExportAnimationCommand), nameof(ShareImageCommand), nameof(ShareAnimationCommand), nameof(ExportCardCommand), nameof(ExportProblemCommand))]
    private bool _isBusy;

    public ShareViewModel(
        GameViewModel game,
        IFileDialogService? files = null,
        Func<IReadOnlyDictionary<GameNode, MoveQuality>>? verdicts = null,
        Func<bool>? coordinates = null,
        IClipboardService? clipboard = null,
        Func<string, Task>? openLink = null,
        ISettingsService? settings = null)
    {
        _settings = settings;
        _hideNames = settings?.Current.ShareHideNames ?? false;
        _showBrand = settings?.Current.ShareShowBrand ?? true;
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _files = files;
        _verdicts = verdicts;
        _coordinates = coordinates ?? (() => true);
        _clipboard = clipboard;
        _openLink = openLink;
    }

    /// <summary>The system share sheet exists (Android): "Share…" items appear next to "Save…".</summary>
    public static bool CanShareSheet => PlatformInfo.ShareFile is not null;

    /// <summary>The shown position as a Sensei's Library diagram.</summary>
    public string DiagramText() => TextDiagram.Senseis(_game.Board, _game.LastMove, _game.Markers, coordinates: true, title: Tr.F("Share.DiagramTitle", _game.MoveNumber));

    /// <summary>The shown position as a code block for chats.</summary>
    public string CodeBlockText() => TextDiagram.CodeBlock(_game.Board, _game.LastMove);

    /// <summary>The shown position as drawn on the board (last move, marks, move numbers when they are on).</summary>
    public BoardShot CurrentShot() => new(_game.Board, _game.LastMove, _game.Markers, _game.MoveNumberLabels.Count > 0 ? _game.MoveNumberLabels : null);

    /// <summary>The PNG of the shown position.</summary>
    public byte[] ImagePng() => BoardPicture.Png(BoardPicture.Render(CurrentShot(), ImagePixels, _coordinates()));

    /// <summary>
    /// The line through the shown move, from the start to its end: one frame per move, longer on captures and on
    /// KataGo's big mistakes (circled), and a long last frame.
    /// </summary>
    public IReadOnlyList<GifFrame> AnimationFrames(int pixels = AnimationPixels)
    {
        GameNode end = _game.CurrentNode;
        while (end.Children.Count > 0)
        {
            end = end.Children[0];
        }

        IReadOnlyDictionary<GameNode, MoveQuality> verdicts = _verdicts?.Invoke() ?? new Dictionary<GameNode, MoveQuality>();
        bool coordinates = _coordinates();
        BoardStyle? style = BoardPicture.CurrentStyle();
        var frames = new List<GifFrame>();
        BoardState? before = null;
        int size = _game.Cursor.BoardSize;
        foreach (GameNode node in GameCursor.Path(end))
        {
            if (node.Parent is not null && !node.HasMove)
            {
                continue; // setup and comment-only nodes: no new picture
            }

            BoardState board = _game.Cursor.GetBoard(node);
            Point? move = node.GetMove(size)?.Point;
            bool mistake = verdicts.TryGetValue(node, out MoveQuality q) && q == MoveQuality.Blunder;
            bool captured = before is not null && board.BlackCaptures + board.WhiteCaptures > before.BlackCaptures + before.WhiteCaptures;
            IReadOnlyList<Markup>? marks = mistake && move is { } m ? [new Markup(m, MarkupKind.Circle)] : null;
            byte[] pixelsBgra = BoardPicture.Bgra(BoardPicture.Render(new BoardShot(board, move, marks), pixels, coordinates, style));
            frames.Add(new GifFrame(pixelsBgra, mistake || captured ? BigMoveDelay : MoveDelay));
            before = board;
        }

        if (frames.Count > 0)
        {
            frames[^1] = frames[^1] with { Delay = LastFrameDelay };
        }

        return frames;
    }

    private bool CanExport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportImage()
    {
        if (_files is null || await _files.PickExportToSaveAsync(SuggestedName(Tr.T("Share.ImageSuffix"), "png"), ExportFileType.Png) is not { } path)
        {
            return;
        }

        await Save(path, ImagePng());
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAnimation()
    {
        if (_files is null || await _files.PickExportToSaveAsync(SuggestedName(Tr.T("Share.AnimationSuffix"), "gif"), ExportFileType.Gif) is not { } path)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _game.ShowStatus(Tr.T("Share.Rendering"));
            IReadOnlyList<GifFrame> frames = AnimationFrames();
            byte[] gif = await Task.Run(() => GifEncoder.Encode(AnimationPixels, AnimationPixels, frames));
            await Save(path, gif);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ShareImage() => ShareFile(ImagePng(), "Share.ImageSuffix", ExportFileType.Png);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ShareAnimation()
    {
        IsBusy = true;
        try
        {
            _game.ShowStatus(Tr.T("Share.Rendering"));
            IReadOnlyList<GifFrame> frames = AnimationFrames();
            byte[] gif = await Task.Run(() => GifEncoder.Encode(AnimationPixels, AnimationPixels, frames));
            await ShareFile(gif, "Share.AnimationSuffix", ExportFileType.Gif);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The words of a card for the shown move: players, move, KataGo's verdict, the study note or comment.</summary>
    public CardText MomentText()
    {
        GameNode node = _game.CurrentNode;
        var subtitle = new List<string> { Tr.F("Share.DiagramTitle", _game.MoveNumber) };
        if (node.GetMove(_game.Cursor.BoardSize) is { } move)
        {
            subtitle.Add((move.Color == Stone.Black ? Tr.T("Common.Black") : Tr.T("Common.White")) + " " + (move.Point is { } p ? p.ToHuman(_game.Board.Height) : Tr.T("Viewer.Pass")));
        }

        if (node.Children.Count == 0 && _game.Tree.Info.Result is { Length: > 0 } result)
        {
            subtitle.Add(result);
        }

        string verdict = _verdicts?.Invoke() is { } v && v.TryGetValue(node, out MoveQuality q) ? Tr.F("Share.Verdict", AnalysisViewModel.QualityName(q)) : string.Empty;
        string note = Hoshi.Sgf.Study.StudyStore.Read(node).Pins.FirstOrDefault()?.Text ?? node.GetValue("C") ?? string.Empty;
        return new CardText($"{Options.Black(_game.Tree)} vs {Options.White(_game.Tree)}", string.Join(" · ", subtitle), verdict, note.Trim(), ShowBrand);
    }

    /// <summary>The share card of the shown move.</summary>
    public byte[] MomentCardPng(CardFormat format) =>
        BoardPicture.Png(ShareCard.Render(CurrentShot(), MomentText(), format, _coordinates()));

    /// <summary>
    /// A problem from the shown move: the position before it, "Black/White to play", and the move as the answer, to
    /// paste hidden as a spoiler. Null when the shown node is not a move.
    /// </summary>
    public (byte[] Png, string Spoiler)? Problem()
    {
        GameNode node = _game.CurrentNode;
        if (node.Parent is not { } parent || node.GetMove(_game.Cursor.BoardSize) is not { Point: { } answer } move)
        {
            return null;
        }

        BoardState before = _game.Cursor.GetBoard(parent);
        string toPlay = Tr.F("Tsumego.ToPlay", Tr.T(move.Color == Stone.White ? "Common.White" : "Common.Black"));
        string human = answer.ToHuman(before.Height);
        var text = new CardText(toPlay, Tr.T("Share.ProblemSubtitle"), string.Empty, string.Empty, ShowBrand);
        byte[] png = BoardPicture.Png(ShareCard.Render(new BoardShot(before, parent.GetMove(_game.Cursor.BoardSize)?.Point), text, CardFormat.Square, coordinates: true));
        return (png, Tr.F("Share.Spoiler", toPlay, human));
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCard(string? format)
    {
        CardFormat f = Enum.TryParse(format, out CardFormat parsed) ? parsed : CardFormat.Square;
        byte[] png = MomentCardPng(f);
        if (PlatformInfo.ShareFile is not null)
        {
            await ShareFile(png, "Share.CardSuffix", ExportFileType.Png);
        }
        else if (_files is not null && await _files.PickExportToSaveAsync(SuggestedName(Tr.T("Share.CardSuffix"), "png"), ExportFileType.Png) is { } path)
        {
            await Save(path, png);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportProblem()
    {
        if (Problem() is not { } problem)
        {
            _game.ShowStatus(Tr.T("Share.ProblemNeedsMove"));
            return;
        }

        if (PlatformInfo.ShareFile is not null)
        {
            await ShareFile(problem.Png, "Share.ProblemSuffix", ExportFileType.Png);
        }
        else if (_files is null || await _files.PickExportToSaveAsync(SuggestedName(Tr.T("Share.ProblemSuffix"), "png"), ExportFileType.Png) is not { } path)
        {
            return;
        }
        else
        {
            await Save(path, problem.Png);
        }

        if (_clipboard is not null)
        {
            await _clipboard.SetTextAsync(problem.Spoiler);
            _game.ShowStatus(Tr.T("Share.SpoilerCopied"));
        }
    }

    /// <summary>The game with its variations, comments and study notes as one web page.</summary>
    public string WebViewerHtml() => WebViewer.Html(_game.Tree, _game.CurrentNode, Options);

    [RelayCommand]
    private async Task ExportWebViewer()
    {
        if (_files is null || await _files.PickExportToSaveAsync(SuggestedName(Tr.T("Share.ViewerSuffix"), "html"), ExportFileType.Html) is not { } path)
        {
            return;
        }

        await Save(path, new System.Text.UTF8Encoding(false).GetBytes(WebViewerHtml()));
    }

    [RelayCommand]
    private Task ShareWebViewer() => ShareFile(new System.Text.UTF8Encoding(false).GetBytes(WebViewerHtml()), "Share.ViewerSuffix", ExportFileType.Html);

    /// <summary>The game as SGF: to the clipboard (desktop) or the share sheet (phone).</summary>
    [RelayCommand]
    private Task CopySgf() => CopyText(SgfWriter.Write(_game.Tree), "Share.SgfCopied");

    [RelayCommand]
    private Task CopyDiagram() => CopyText(DiagramText(), "Share.DiagramCopied");

    [RelayCommand]
    private Task CopyCodeBlock() => CopyText(CodeBlockText(), "Share.DiagramCopied");

    /// <summary>Opens what is on the clipboard: an SGF game, or a link (or number) of an OGS game.</summary>
    [RelayCommand]
    private async Task PasteSgf()
    {
        string? text = _clipboard is null ? null : await _clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            _game.ShowStatus(Tr.T("Share.NothingToPaste"));
        }
        else if (text.Contains("(;", StringComparison.Ordinal))
        {
            await _game.OpenSgfTextAsync(text, Tr.T("Share.Clipboard"));
        }
        else if (Hoshi.Ogs.OgsLinks.TryGameId(text) is not null && _openLink is not null)
        {
            await _openLink(text.Trim());
        }
        else
        {
            _game.ShowStatus(Tr.T("Share.NothingToPaste"));
        }
    }

    private async Task CopyText(string text, string doneKey)
    {
        if (PlatformInfo.ShareText is { } share)
        {
            await share(text, Tr.T("Share.Menu"));
            return;
        }

        if (_clipboard is not null)
        {
            await _clipboard.SetTextAsync(text);
            _game.ShowStatus(Tr.T(doneKey));
        }
    }

    private async Task ShareFile(byte[] bytes, string suffixKey, ExportFileType type)
    {
        string name = SuggestedName(Tr.T(suffixKey), type.Extension);
        if (PlatformInfo.ShareFile is not { } share)
        {
            // No share sheet here: save it like "Export…".
            if (_files is not null && await _files.PickExportToSaveAsync(name, type) is { } picked)
            {
                await Save(picked, bytes);
            }

            return;
        }

        try
        {
            Directory.CreateDirectory(PlatformInfo.ShareDirectory);
            string path = Path.Combine(PlatformInfo.ShareDirectory, name);
            await File.WriteAllBytesAsync(path, bytes);
            await share(path, type.Mime, Tr.T("Share.Menu"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _game.ShowStatus(Tr.F("Share.SaveFailed", ex.Message));
        }
    }

    private async Task Save(string path, byte[] bytes)
    {
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await _files!.FinishSaveAsync(path);
            _game.ShowStatus(Tr.F("Share.Saved", Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _game.ShowStatus(Tr.F("Share.SaveFailed", ex.Message));
        }
    }

    private string SuggestedName(string suffix, string extension)
    {
        string b = Options.Black(_game.Tree);
        string w = Options.White(_game.Tree);
        string name = $"{b} vs {w} — {suffix}.{extension}";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
