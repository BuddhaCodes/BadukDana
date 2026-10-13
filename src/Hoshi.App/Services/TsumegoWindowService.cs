using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows the problems (a window on the desktop, a page on phones).</summary>
public interface ITsumegoWindowService
{
    void Show();
}

public sealed class TsumegoWindowService(TsumegoViewModel tsumego) : ITsumegoWindowService
{
    private TsumegoWindow? _window;

    public void Show()
    {
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        _window = new TsumegoWindow { DataContext = tsumego };
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
