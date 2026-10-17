namespace Hoshi.App.Services;

/// <summary>The system clipboard (text only). Abstracted so view models stay testable.</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);

    Task<string?> GetTextAsync();
}

public sealed class AvaloniaClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) =>
        MainWindowLocator.Top?.Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;

    public async Task<string?> GetTextAsync() =>
        MainWindowLocator.Top?.Clipboard is { } clipboard ? await clipboard.GetTextAsync() : null;
}
