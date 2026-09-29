using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows the (single, non-modal) "Jugar en línea" window.</summary>
public interface ILobbyWindowService
{
    void Show();
}

public sealed class LobbyWindowService(LobbyViewModel lobby) : ILobbyWindowService
{
    private LobbyWindow? _window;

    public void Show()
    {
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        _window = new LobbyWindow { DataContext = lobby };
        _window.Closed += (_, _) => _window = null;
        if (MainWindowLocator.MainWindow is { } owner)
        {
            _window.Show(owner);
        }
        else
        {
            _window.Show();
        }
    }
}
