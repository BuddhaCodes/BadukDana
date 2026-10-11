namespace Hoshi.App.Services;

/// <summary>What kind of device Hoshi runs on (phones get the single-view layout and touch-first defaults).</summary>
public static class PlatformInfo
{
    /// <summary>True on Android. Settable so tests can render and exercise the phone layout on the desktop.</summary>
    public static bool IsPhone { get; set; } = OperatingSystem.IsAndroid();

    /// <summary>Tap and confirm, as saved or the platform's default.</summary>
    public static bool ConfirmMoves(AppSettings settings) => settings.ConfirmMoves ?? IsPhone;
}
