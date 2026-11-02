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
/// "Photo of the board": open a photo (on the phone, the picker can also take one), tap the four corner
/// intersections, choose the size, read the stones, and open the position in the editor to fix what the photo hid.
/// </summary>
public sealed partial class BoardPhotoViewModel : ViewModelBase
{
    /// <summary>Photos are read at most this wide: plenty for 19 lines, and light on a phone's memory.</summary>
    public const int MaxWidth = 1600;

    private readonly GameViewModel _game;
    private readonly IFileDialogService? _files;
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
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private bool _hasResult;

    [ObservableProperty]
    private bool _whiteToPlay;

    public BoardPhotoViewModel(GameViewModel game, IFileDialogService? files = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _files = files;
        Status = Tr.T("Photo.Pick");
    }

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

    /// <summary>A tap on the photo (image pixels): the next corner; with four, the stones are read.</summary>
    public void Tap(double x, double y)
    {
        if (_pixels is null)
        {
            return;
        }

        if (Corners.Count >= 4)
        {
            Reset();
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

    /// <summary>The position went to the editor: the window or page can close.</summary>
    public event EventHandler? Opened;
}
