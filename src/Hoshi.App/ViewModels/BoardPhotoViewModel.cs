using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Photo;
using Hoshi.Core;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>A corner or a recognized stone drawn over the photo (image pixel coordinates).</summary>
public sealed record PhotoMark(double X, double Y, Stone Stone, string Label);

/// <summary>
/// "Photo of the board": take a photo (phone camera) or open one, check the four corner intersections Hoshi found by
/// itself (a tap moves the nearest one; "Start over" taps them all by hand), choose the size, rotate if the photo was
/// taken from another side, and open the position: to study it (study panel open, kept in the game library) or in
/// the editor to fix what the photo hid.
/// </summary>
public sealed partial class BoardPhotoViewModel : ViewModelBase
{
    /// <summary>Photos are read at most this wide: plenty for 19 lines, and light on a phone's memory.</summary>
    public const int MaxWidth = 1600;

    private readonly GameViewModel _game;
    private readonly IFileDialogService? _files;
    private readonly StudyViewModel? _study;
    private readonly Func<Hoshi.Sgf.GameTree, bool>? _keep;
    private PhotoPixels? _pixels;
    private RecognizedBoard? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private Bitmap? _photo;

    [ObservableProperty]
    private int _size = 19;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(StudyCommand), nameof(RotateCommand))]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isFinding;

    [ObservableProperty]
    private bool _whiteToPlay;

    /// <param name="game">Where the position opens.</param>
    /// <param name="files">The photo picker.</param>
    /// <param name="study">Opened by "Study it".</param>
    /// <param name="keep">Saves the position in the game library (true when it was kept).</param>
    public BoardPhotoViewModel(GameViewModel game, IFileDialogService? files = null, StudyViewModel? study = null, Func<Hoshi.Sgf.GameTree, bool>? keep = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _files = files;
        _study = study;
        _keep = keep;
        Status = Tr.T("Photo.Pick");
    }

    /// <summary>The phone has a camera Hoshi can use.</summary>
    public bool CanTakePhoto => PlatformInfo.TakePhoto is not null;

    public IReadOnlyList<int> Sizes { get; } = [9, 13, 19];

    public bool HasPhoto => Photo is not null;

    /// <summary>The tapped corners (top left, top right, bottom right, bottom left) and, once read, the stones.</summary>
    public ObservableCollection<PhotoMark> Marks { get; } = [];

    public IReadOnlyList<(double X, double Y)> Corners => [.. Marks.Where(m => m.Label.Length > 0).Select(m => (m.X, m.Y))];

    public int PixelWidth => _pixels?.Width ?? 0;

    public int PixelHeight => _pixels?.Height ?? 0;

    [RelayCommand]
    private async Task Pick()
    {
        if (_files is null || await _files.PickImageToOpenAsync() is not { } path)
        {
            return;
        }

        await LoadFileAsync(path);
    }

    /// <summary>The phone's camera app takes the photo; it comes back here (nothing leaves the phone).</summary>
    [RelayCommand]
    private async Task TakePhoto()
    {
        if (PlatformInfo.TakePhoto is not { } take)
        {
            return;
        }

        string? path;
        try
        {
            path = await take();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = Tr.F("Photo.Unreadable", ex.Message);
            return;
        }

        if (path is not null)
        {
            await LoadFileAsync(path);
        }
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            using var probe = new Bitmap(stream);
            stream.Position = 0;
            Load(probe.PixelSize.Width > MaxWidth ? Bitmap.DecodeToWidth(stream, MaxWidth) : new Bitmap(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = Tr.F("Photo.Unreadable", ex.Message);
            return;
        }

        await FindCornersAsync();
    }

    /// <summary>
    /// Looks for the board by itself and, when found, places the four corners and reads the stones; the player checks
    /// them (a tap moves the nearest corner). When not found, the player taps the corners as before.
    /// </summary>
    public async Task FindCornersAsync()
    {
        if (_pixels is not { } pixels)
        {
            return;
        }

        IsFinding = true;
        Status = Tr.T("Photo.Finding");
        int size = Size;
        IReadOnlyList<(double X, double Y)>? found;
        try
        {
            found = await Task.Run(() => BoardFinder.FindCorners(pixels, size));
        }
        finally
        {
            IsFinding = false;
        }

        if (!ReferenceEquals(pixels, _pixels) || Corners.Count > 0)
        {
            return; // another photo arrived, or the player started tapping meanwhile
        }

        if (found is null)
        {
            Status = Tr.T("Photo.NotFound");
            return;
        }

        SetCorners(found);
        if (HasResult)
        {
            Status = Tr.F("Photo.Found", _result!.Count(Stone.Black), _result.Count(Stone.White));
        }
    }

    private void SetCorners(IReadOnlyList<(double X, double Y)> corners)
    {
        Marks.Clear();
        for (int i = 0; i < 4; i++)
        {
            Marks.Add(new PhotoMark(corners[i].X, corners[i].Y, Stone.Empty, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Read();
    }

    /// <summary>
    /// The photo was taken from another side of the board: turns the position a quarter (the corners are the same
    /// four points; which one is the top left changes).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Rotate()
    {
        IReadOnlyList<(double X, double Y)> c = Corners;
        if (c.Count == 4)
        {
            SetCorners([c[1], c[2], c[3], c[0]]);
        }
    }

    /// <summary>Takes a photo already decoded (and tests).</summary>
    public void Load(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        byte[] bgra = Services.Export.BoardPicture.Bgra(bitmap);
        if (bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888)
        {
            for (int i = 0; i < bgra.Length; i += 4)
            {
                (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            }
        }

        Load(bitmap, new PhotoPixels(bitmap.PixelSize.Width, bitmap.PixelSize.Height, bgra));
    }

    public void Load(Bitmap? shown, PhotoPixels pixels)
    {
        Photo = shown;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        Marks.Clear();
        _result = null;
        HasResult = false;
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
        Status = Tr.T("Photo.TapCorners");
    }

    /// <summary>
    /// A tap on the photo (image pixels): the next corner; with four, the stones are read. Once there are four (found
    /// or tapped), a tap moves the nearest corner there.
    /// </summary>
    public void Tap(double x, double y)
    {
        if (_pixels is null)
        {
            return;
        }

        IReadOnlyList<(double X, double Y)> corners = Corners;
        if (corners.Count >= 4)
        {
            int nearest = Enumerable.Range(0, 4).MinBy(i => Math.Pow(corners[i].X - x, 2) + Math.Pow(corners[i].Y - y, 2));
            SetCorners([.. corners.Select((c, i) => i == nearest ? (x, y) : c)]);
            return;
        }

        string[] names = ["1", "2", "3", "4"];
        Marks.Add(new PhotoMark(x, y, Stone.Empty, names[Corners.Count]));
        if (Corners.Count == 4)
        {
            Read();
        }
        else
        {
            Status = Tr.F("Photo.CornerNext", Tr.T("Photo.Corner." + Corners.Count));
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Marks.Clear();
        _result = null;
        HasResult = false;
        Status = _pixels is null ? Tr.T("Photo.Pick") : Tr.T("Photo.TapCorners");
    }

    partial void OnSizeChanged(int value)
    {
        if (Corners.Count == 4)
        {
            Read();
        }
    }

    private void Read()
    {
        if (_pixels is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<(double X, double Y)> corners = Corners;
            _result = BoardRecognizer.Recognize(_pixels, corners, Size);
        }
        catch (ArgumentException)
        {
            Status = Tr.T("Photo.BadCorners");
            return;
        }

        // Show the stones over the photo where they were read.
        foreach (PhotoMark old in Marks.Where(m => m.Label.Length == 0).ToList())
        {
            Marks.Remove(old);
        }

        double[] h = BoardRecognizer.Homography(Corners);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (_result.Stones[x, y] != Stone.Empty)
                {
                    (double px, double py) = BoardRecognizer.Map(h, (double)x / (Size - 1), (double)y / (Size - 1));
                    Marks.Add(new PhotoMark(px, py, _result.Stones[x, y], string.Empty));
                }
            }
        }

        HasResult = true;
        Status = Tr.F("Photo.Read", _result.Count(Stone.Black), _result.Count(Stone.White));
    }

    /// <summary>Opens the position in the editor (edit mode on), to correct what the photo got wrong.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Open()
    {
        if (_result is null)
        {
            return;
        }

        _game.Load(BoardRecognizer.ToGame(_result, WhiteToPlay ? Stone.White : Stone.Black), path: null);
        if (!_game.IsEditMode)
        {
            _game.ToggleEditModeCommand.Execute(null);
        }

        _game.ShowStatus(Tr.T("Photo.Opened"));
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "Study it": the position opens with the study panel (notes, drawings, "what if…" lines) and is kept in the game
    /// library, so it can be found again. Your live OGS game is never replaced by it: the board leaves that game first,
    /// as with any game opened.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Study()
    {
        if (_result is null)
        {
            return;
        }

        Hoshi.Sgf.GameTree tree = BoardRecognizer.ToGame(_result, WhiteToPlay ? Stone.White : Stone.Black);
        tree.Root.SetValue("GN", Tr.F("Photo.GameName", DateTime.Now.ToString("g", System.Globalization.CultureInfo.CurrentCulture)));
        _game.Load(tree, path: null);
        if (_game.IsEditMode)
        {
            _game.ToggleEditModeCommand.Execute(null);
        }

        if (_study is { IsActive: false })
        {
            _study.IsActive = true;
        }

        bool kept = _keep?.Invoke(tree) ?? false;
        _game.ShowStatus(Tr.T(kept ? "Photo.Studying" : "Photo.StudyOpened"));
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The position went to the editor: the window or page can close.</summary>
    public event EventHandler? Opened;
}
