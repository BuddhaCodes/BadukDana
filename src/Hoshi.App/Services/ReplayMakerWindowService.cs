using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows the replay maker (desktop only).</summary>
public interface IReplayMakerWindowService
{
    void Show(ReplayMakerViewModel maker);
}

public sealed class ReplayMakerWindowService : IReplayMakerWindowService
{
    private ReplayMakerWindow? _window;

    public void Show(ReplayMakerViewModel maker)
    {
        ArgumentNullException.ThrowIfNull(maker);
        maker.Reset();
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        _window = new ReplayMakerWindow { DataContext = maker };
        _window.Closed += (_, _) =>
        {
            maker.CancelCommand.Execute(null);
            _window = null;
        };
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
