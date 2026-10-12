namespace Hoshi.App.Services;

/// <summary>What kind of device Hoshi runs on (phones get the single-view layout and touch-first defaults).</summary>
public static class PlatformInfo
{
    /// <summary>True on Android. Settable so tests can render and exercise the phone layout on the desktop.</summary>
    public static bool IsPhone { get; set; } = OperatingSystem.IsAndroid();

    /// <summary>Tap and confirm, as saved or the platform's default.</summary>
    public static bool ConfirmMoves(AppSettings settings) => settings.ConfirmMoves ?? IsPhone;

    /// <summary>Coordinates around the board, as saved or the platform's default (hidden on phones).</summary>
    public static bool ShowCoordinates(AppSettings settings) => settings.ShowCoordinates ?? !IsPhone;

    /// <summary>A short tap of the vibration motor (set by Android); null where there is none.</summary>
    public static Action? Vibrate { get; set; }

    /// <summary>Keeps the screen from dimming while true (set by Android); null where it does not apply.</summary>
    public static Action<bool>? KeepScreenOn { get; set; }
}
