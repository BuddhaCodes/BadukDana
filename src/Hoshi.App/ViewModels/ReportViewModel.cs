using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Diagnostics;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// "Report a problem" (menu) and, after a crash, a quiet card on the next start. Both open a pre-filled GitHub issue
/// in the browser: nothing is sent by Hoshi itself, and secrets and the home folder are removed from the text.
/// </summary>
public sealed partial class ReportViewModel : ViewModelBase
{
    private readonly IBrowserLauncher? _browser;
    private readonly IClipboardService? _clipboard;
    private readonly string _logDirectory;
    private readonly Func<CrashRecord?> _pending;
    private readonly Action _clear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrash), nameof(CrashText))]
    private CrashRecord? _crash;

    public ReportViewModel(IBrowserLauncher? browser = null, IClipboardService? clipboard = null, string? logDirectory = null,
        Func<CrashRecord?>? pending = null, Action? clear = null)
    {
        _browser = browser;
        _clipboard = clipboard;
        _logDirectory = logDirectory ?? Path.Combine(AppPaths.DataDirectory, "logs");
        _pending = pending ?? CrashGuard.Pending;
        _clear = clear ?? CrashGuard.Clear;

        // Only a crash that closed Hoshi is worth a card on the next start; smaller errors wait for the menu.
        Crash = _pending() is { Fatal: true } fatal ? fatal : null;
    }

    public bool HasCrash => Crash is not null;

    public string CrashText => Crash is { } c ? Tr.F("Report.Crashed", c.When.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)) : string.Empty;

    /// <summary>"Hoshi {version} · Windows 11 (X64)", the line that heads every report.</summary>
    public static string SystemLine => PlatformInfo.IsPhone
        ? $"Android · {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"
        : $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>The issue text as it would be sent (for "Copy the report").</summary>
    public string BuildBody() => ProblemReport.Body(_pending(), ProblemReport.LogTail(_logDirectory), CrashGuard.Version, SystemLine);

    [RelayCommand]
    private async Task Report()
    {
        Uri url = ProblemReport.IssueUrl(_pending(), ProblemReport.LogTail(_logDirectory), CrashGuard.Version, SystemLine);
        if (_browser is not null)
        {
            try
            {
                await _browser.OpenAsync(url, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Hoshi.Ogs.Auth.OgsAuthException)
            {
                // No browser: the clipboard still has the text.
                await Copy();
                return;
            }
        }

        Dismiss();
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (_clipboard is not null)
        {
            await _clipboard.SetTextAsync(BuildBody());
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _clear();
        Crash = null;
    }
}
