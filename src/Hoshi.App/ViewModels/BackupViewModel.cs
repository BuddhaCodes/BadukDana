using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Backup;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Menu → "Export a backup…" / "Import a backup…": the player's games, studies and progress in one <c>.hoshi</c>
/// file, to keep safe or to carry between a computer and a phone. Imports merge (nothing is lost) and finish at the
/// next start.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly string _dataDirectory;
    private readonly Func<string> _version;

    public BackupViewModel(IFileDialogService? files = null, IDialogService? dialogs = null, string? dataDirectory = null, Func<string>? version = null)
    {
        _files = files;
        _dialogs = dialogs;
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        _version = version ?? (() => Services.Updates.UpdatePlatform.Display(Services.Updates.UpdatePlatform.CurrentVersion));
    }

    /// <summary>Raised with a line for the status bar.</summary>
    public event EventHandler<string>? Status;

    /// <summary>The line to show at start about an import merged then (or that failed); null when none.</summary>
    public static string? StartupMessage(BackupSummary? summary, string? error) =>
        error is not null ? Tr.F("Backup.Failed", error)
        : summary is null ? null
        : Tr.F("Backup.Imported", summary.Games, summary.JosekiCards, summary.ProblemCards, summary.Reviews);

    [RelayCommand]
    private async Task Export()
    {
        if (_files is null || await _files.PickExportToSaveAsync($"hoshi-{DateTime.Now:yyyy-MM-dd}.{BackupPackage.Extension}", ExportFileType.Backup) is not { } path)
        {
            return;
        }

        try
        {
            await Task.Run(() => BackupPackage.Export(_dataDirectory, path, _version()));
            await _files.FinishSaveAsync(path);
            Status?.Invoke(this, Tr.T("Backup.Exported"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Fail(ex.Message);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (_files is null || await _files.PickBackupToOpenAsync() is not { } path)
        {
            return;
        }

        bool preferences = _dialogs is not null && await _dialogs.ConfirmAsync(Tr.T("Backup.PrefsTitle"), Tr.T("Backup.PrefsAsk"));
        try
        {
            await Task.Run(() => BackupPackage.Stage(path, _dataDirectory, preferences));
            Status?.Invoke(this, Tr.T("Backup.Staged"));
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Backup.Import").TrimEnd('…'), Tr.T("Backup.Staged"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            await Fail(ex.Message);
        }
    }

    private async Task Fail(string message)
    {
        string text = Tr.F("Backup.Failed", message);
        Status?.Invoke(this, text);
        if (_dialogs is not null)
        {
            await _dialogs.ShowErrorAsync(Tr.T("Backup.Type"), text);
        }
    }
}
