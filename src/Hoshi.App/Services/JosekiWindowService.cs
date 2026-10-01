using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

/// <summary>Shows the (single, non-modal) joseki trainer.</summary>
public interface IJosekiWindowService
{
    void Show();
}

public sealed class JosekiWindowService(JosekiTrainerViewModel trainer) : IJosekiWindowService
{
    private JosekiTrainerWindow? _window;

    public void Show()
    {
        trainer.Start();
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        _window = new JosekiTrainerWindow { DataContext = trainer };
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
