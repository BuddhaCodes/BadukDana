using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Hoshi.App.Services;

/// <summary>
/// "Your move" outside Hoshi's window on the desktop, for when the player is in another app (correspondence games
/// above all). Nothing happens while Hoshi's window is in front: the in-app card is enough there.
/// Windows: the taskbar button flashes until Hoshi comes to the front (no registration or extra package needed).
/// macOS: a Notification Centre banner (<c>osascript</c>). Linux: <c>notify-send</c> (libnotify), when installed.
/// </summary>
public static class DesktopNotifier
{
    /// <summary>Installs itself as <see cref="PlatformInfo.Notify"/> unless a platform (Android) already did.</summary>
    public static void Install()
    {
        if (PlatformInfo.Notify is null && !PlatformInfo.IsPhone)
        {
            PlatformInfo.Notify = (title, text, _) => Dispatcher.UIThread.Post(() => Show(title, text));
        }
    }

    /// <summary>Shows the notice if Hoshi's main window is not the active one. Call on the UI thread.</summary>
    public static void Show(string title, string text)
    {
        Window? window = MainWindowLocator.MainWindow;
        if (window is { IsActive: true })
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (window?.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
                {
                    Flash(hwnd);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                Launch("osascript", ["-e", $"display notification \"{AppleScript(text)}\" with title \"{AppleScript(title)}\""]);
            }
            else if (OperatingSystem.IsLinux())
            {
                Launch("notify-send", ["--app-name=Hoshi", "--icon=hoshi", title, text]);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // No notifier on this system: the in-app card and the sound remain.
            Trace.WriteLine($"Hoshi: desktop notification unavailable: {ex.Message}");
        }
    }

    /// <summary>Quotes text for an AppleScript string literal.</summary>
    internal static string AppleScript(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void Launch(string file, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        using Process? process = Process.Start(info);
    }

    private static void Flash(IntPtr hwnd)
    {
        var info = new NativeMethods.FlashInfo
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.FlashInfo>(),
            hwnd = hwnd,
            dwFlags = NativeMethods.FlashTray | NativeMethods.FlashUntilForeground,
            uCount = 0,
            dwTimeout = 0,
        };
        NativeMethods.FlashWindowEx(ref info);
    }

#pragma warning disable SYSLIB1054 // One rarely called user32 function; DllImport keeps it simple.
    private static class NativeMethods
    {
        public const uint FlashTray = 0x2;
        public const uint FlashUntilForeground = 0xC;

        [StructLayout(LayoutKind.Sequential)]
        public struct FlashInfo
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FlashWindowEx(ref FlashInfo info);
    }
#pragma warning restore SYSLIB1054
}
