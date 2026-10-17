using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hoshi.App.Controls;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Services.Export;

/// <summary>What to draw on an exported board.</summary>
public sealed record BoardShot(
    BoardState Board,
    Point? LastMove = null,
    IReadOnlyList<Markup>? Markers = null,
    IReadOnlyDictionary<Point, int>? MoveNumbers = null);

/// <summary>
/// Draws a board off screen with the same control as the main window (the theme's goban and stones), for images,
/// animations and share cards. Must run on the UI thread.
/// </summary>
public static class BoardPicture
{
    /// <summary>The theme's board, or Classic when there is no app (tests).</summary>
    public static BoardStyle? CurrentStyle() =>
        Application.Current?.TryGetResource("Theme.Board", null, out object? value) == true ? value as BoardStyle : null;

    public static RenderTargetBitmap Render(BoardShot shot, int pixels, bool coordinates, BoardStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 16);
        var control = new GoBoardControl
        {
            Board = shot.Board,
            LastMove = shot.LastMove,
            Markers = shot.Markers,
            MoveNumbers = shot.MoveNumbers,
            ShowCoordinates = coordinates,
            BoardStyle = style ?? CurrentStyle(),
            Animate = false,
            Effects = EffectsLevel.Off,
            IsInteractive = false,
            Width = pixels,
            Height = pixels,
        };
        control.Measure(new Size(pixels, pixels));
        control.Arrange(new Rect(0, 0, pixels, pixels));
        var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        bitmap.Render(control);
        return bitmap;
    }

    /// <summary>The bitmap's pixels as straight BGRA (what <see cref="GifEncoder"/> reads).</summary>
    public static byte[] Bgra(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        PixelSize size = bitmap.PixelSize;
        int stride = size.Width * 4;
        byte[] pixels = new byte[stride * size.Height];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // The board is opaque, but its edge may not be: un-premultiply and put it on white so GIFs keep their colours.
        if (bitmap.AlphaFormat is AlphaFormat.Premul or null)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                if (a < 255)
                {
                    int white = 255 - a;
                    pixels[i] = (byte)Math.Min(255, pixels[i] + white);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + white);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + white);
                    pixels[i + 3] = 255;
                }
            }
        }

        return pixels;
    }

    public static byte[] Png(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
