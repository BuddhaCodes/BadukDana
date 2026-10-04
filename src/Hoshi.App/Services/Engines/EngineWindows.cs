using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services.Engines;

/// <summary>The engine dialogs: "Play against an engine" (modal) and the GTP console (single, non-modal).</summary>
public interface IEngineWindows
{
    /// <summary>Shows the dialog; true when the user pressed Start.</summary>
    Task<bool> AskNewGameAsync(NewEngineGameViewModel options);

    void ShowConsole();
}

public sealed class EngineWindows(GtpConsoleViewModel console) : IEngineWindows
{
    private GtpConsoleWindow? _console;

    public async Task<bool> AskNewGameAsync(NewEngineGameViewModel options)
    {
        if (MainWindowLocator.MainWindow is not { } owner)
        {
            return false;
        }

        return await new NewEngineGameWindow { DataContext = options }.ShowDialog<bool>(owner);
    }

    public void ShowConsole()
    {
        if (_console is { } open)
        {
            open.Activate();
            return;
        }

        _console = new GtpConsoleWindow { DataContext = console };
        _console.Closed += (_, _) => _console = null;
        if (MainWindowLocator.MainWindow is { } owner)
        {
            _console.Show(owner);
        }
        else
        {
            _console.Show();
        }
    }
}
