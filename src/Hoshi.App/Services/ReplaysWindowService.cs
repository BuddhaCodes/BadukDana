using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows the (single, non-modal) "Played games" window.</summary>
public interface IReplaysWindowService
{
    void Show();
}

public sealed class ReplaysWindowService(ReplaysViewModel replays) : IReplaysWindowService
{
    private ReplaysWindow? _window;

    public void Show()
    {
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        _window = new ReplaysWindow { DataContext = replays };
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
