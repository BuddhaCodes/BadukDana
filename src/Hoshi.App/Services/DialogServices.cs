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

    /// <summary>Where to save an exported file (PNG, GIF, web viewer…); null when cancelled or not available.</summary>
    Task<string?> PickExportToSaveAsync(string suggestedName, ExportFileType type) => Task.FromResult<string?>(null);

    /// <summary>
    /// Call after writing to a path a save picker returned. On Android the picked file is a document of the
    /// storage framework, not a path: Hoshi writes a private copy and this sends it there.
    /// </summary>
    Task FinishSaveAsync(string path) => Task.CompletedTask;
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
            .FirstOrDefault(w => w.IsActive) ?? MainWindowLocator.Top;
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
        return files.Count > 0 ? await StorageFiles.LocalPathAsync(files[0]) : null;
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

    /// <summary>
    /// The top level to ask for files, the browser or the clipboard: the main window on the desktop, the view's
    /// top level on the phone (single-view lifetime).
    /// </summary>
    public static TopLevel? Top =>
        MainWindow ?? (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } view } ? TopLevel.GetTopLevel(view) : null);
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
        if (MainWindowLocator.Top?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.T("Dialog.OpenSgf"),
            AllowMultiple = false,
            FileTypeFilter = [Sgf, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? await StorageFiles.LocalPathAsync(files[0]) : null;
    }

    public async Task<string?> PickSgfToSaveAsync(string suggestedName)
    {
        if (MainWindowLocator.Top?.StorageProvider is not { } storage)
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
        return file is null ? null : SavePath(file);
    }

    public async Task<string?> PickHtmlToSaveAsync(string suggestedName)
    {
        if (MainWindowLocator.Top?.StorageProvider is not { } storage)
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
        return file is null ? null : SavePath(file);
    }

    public async Task<string?> PickExportToSaveAsync(string suggestedName, ExportFileType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (MainWindowLocator.Top?.StorageProvider is not { } storage)
        {
            return null;
        }

        var choice = new FilePickerFileType(Tr.T(type.NameKey)) { Patterns = ["*." + type.Extension], MimeTypes = [type.Mime] };
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.T("Share.SaveTitle"),
            SuggestedFileName = suggestedName,
            DefaultExtension = type.Extension,
            FileTypeChoices = [choice],
            ShowOverwritePrompt = true,
        });
        return file is null ? null : SavePath(file);
    }

    // Android: picked files are content:// documents. Opening reads a private copy; saving writes one that
    // FinishSaveAsync sends to the document (every time the same path is saved again).
    private readonly Dictionary<string, IStorageFile> _documents = new(StringComparer.Ordinal);

    public async Task FinishSaveAsync(string path)
    {
        if (!_documents.TryGetValue(path, out IStorageFile? document))
        {
            return;
        }

        await using Stream source = File.OpenRead(path);
        await using Stream target = await document.OpenWriteAsync();
        target.SetLength(0);
        await source.CopyToAsync(target);
    }

    private string SavePath(IStorageFile file)
    {
        if (file.TryGetLocalPath() is { } local)
        {
            return local;
        }

        string path = StorageFiles.PrivateCopyPath(file.Name);
        _documents[path] = file;
        return path;
    }

}

/// <summary>Picked files as paths: Android documents (content://) are copied into Hoshi's own folder first.</summary>
internal static class StorageFiles
{
    public static async Task<string?> LocalPathAsync(IStorageFile file)
    {
        if (file.TryGetLocalPath() is { } local)
        {
            return local;
        }

        string path = PrivateCopyPath(file.Name);
        await using Stream source = await file.OpenReadAsync();
        await using FileStream target = File.Create(path);
        await source.CopyToAsync(target);
        return path;
    }

    public static string PrivateCopyPath(string name)
    {
        string directory = Path.Combine(AppPaths.DataDirectory, "documents", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(directory, safe.Length > 0 ? safe : "game.sgf");
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

/// <summary>A kind of exported file: its extension, MIME type and the name shown in the save dialog.</summary>
public sealed record ExportFileType(string Extension, string Mime, string NameKey)
{
    public static readonly ExportFileType Png = new("png", "image/png", "Share.TypePng");
    public static readonly ExportFileType Gif = new("gif", "image/gif", "Share.TypeGif");
    public static readonly ExportFileType Html = new("html", "text/html", "Dialog.WebPage");
    public static readonly ExportFileType Text = new("txt", "text/plain", "Share.TypeText");
}
