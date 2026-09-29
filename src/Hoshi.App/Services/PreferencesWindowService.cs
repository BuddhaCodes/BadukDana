using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Services;

public interface IPreferencesWindowService
{
    Task ShowAsync();
}

public sealed class PreferencesWindowService(PreferencesViewModel preferences) : IPreferencesWindowService
{
    public async Task ShowAsync()
    {
        var window = new PreferencesWindow { DataContext = preferences };
        if (MainWindowLocator.MainWindow is { } owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
