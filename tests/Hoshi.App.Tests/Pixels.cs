using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hoshi.App.Tests;

internal static class Pixels
{
    /// <summary>Reads one pixel from a rendered headless frame (Bgra8888 or Rgba8888).</summary>
    public static Color Read(WriteableBitmap bitmap, int x, int y)
    {
        using ILockedFramebuffer fb = bitmap.Lock();
        unsafe
        {
            byte* row = (byte*)fb.Address + (y * fb.RowBytes);
            uint px = ((uint*)row)[x];
            return fb.Format == PixelFormat.Rgba8888
                ? Color.FromArgb((byte)(px >> 24), (byte)px, (byte)(px >> 8), (byte)(px >> 16))
                : Color.FromArgb((byte)(px >> 24), (byte)(px >> 16), (byte)(px >> 8), (byte)px);
        }
    }
}
