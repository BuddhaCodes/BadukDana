using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;

namespace Hoshi.App.Services;

/// <summary>Native open/save pickers. Abstracted so view models stay testable.</summary>
public interface IFileDialogService
{
    Task<string?> PickSgfToOpenAsync();

    Task<string?> PickSgfToSaveAsync(string suggestedName);

    /// <summary>Where to save a web page (the study report); null when cancelled or not available.</summary>
    Task<string?> PickHtmlToSaveAsync(string suggestedName) => Task.FromResult<string?>(null);
}

/// <summary>Picks any existing file (e.g. the KataGo executable or network).</summary>
public interface IFilePickerService
{
    Task<string?> PickFileAsync(string title);
}

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickFileAsync(string title)
    {
        TopLevel? top = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .FirstOrDefault(w => w.IsActive) ?? MainWindowLocator.MainWindow;
        if (top?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}

/// <summary>Modal dialogs owned by the main window.</summary>
public interface IDialogService
{
    /// <summary>Shows the game-info editor; returns true when the user confirmed.</summary>
    Task<bool> EditGameInfoAsync(GameInfoViewModel info);

    /// <summary>Asks a yes/no question; returns true for yes.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);
}

internal static class MainWindowLocator
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType Sgf = new("SGF")
    {
        Patterns = ["*.sgf"],
        MimeTypes = ["application/x-go-sgf"],
    };

    public async Task<string?> PickSgfToOpenAsync()
    {
        if (MainWindowLocator.MainWindow?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.T("Dialog.OpenSgf"),
            AllowMultiple = false,
            FileTypeFilter = [Sgf, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSgfToSaveAsync(string suggestedName)
    {
        if (MainWindowLocator.MainWindow?.StorageProvider is not { } storage)
        {
            return null;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.T("Dialog.SaveSgf"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "sgf",
            FileTypeChoices = [Sgf],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickHtmlToSaveAsync(string suggestedName)
    {
        if (MainWindowLocator.MainWindow?.StorageProvider is not { } storage)
        {
            return null;
        }

        var html = new FilePickerFileType(Tr.T("Dialog.WebPage")) { Patterns = ["*.html"], MimeTypes = ["text/html"] };
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.T("Study.SaveReport"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "html",
            FileTypeChoices = [html],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }
}

public sealed class AvaloniaDialogService : IDialogService
{
    public async Task<bool> EditGameInfoAsync(GameInfoViewModel info)
    {
        if (MainWindowLocator.MainWindow is not { } owner)
        {
            return false;
        }

        var dialog = new GameInfoWindow { DataContext = info };
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (MainWindowLocator.MainWindow is not { } owner)
        {
            return false;
        }

        var dialog = new MessageWindow(title, message, confirm: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (MainWindowLocator.MainWindow is { } owner)
        {
            await new MessageWindow(title, message, confirm: false).ShowDialog<bool>(owner);
        }
    }
}
