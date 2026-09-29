using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Native open/save pickers. Abstracted so view models stay testable.</summary>
public interface IFileDialogService
{
    Task<string?> PickSgfToOpenAsync();

    Task<string?> PickSgfToSaveAsync(string suggestedName);
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
            Title = "Abrir SGF",
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
            Title = "Guardar SGF",
            SuggestedFileName = suggestedName,
            DefaultExtension = "sgf",
            FileTypeChoices = [Sgf],
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
