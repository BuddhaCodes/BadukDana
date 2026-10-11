using Avalonia.Controls;
using Avalonia.Interactivity;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

/// <summary>Preferences (theme, boards, analysis, sound, engines), in a window on the desktop or a page on the phone.</summary>
public partial class PreferencesView : UserControl
{
    public PreferencesView()
    {
        InitializeComponent();
    }

    /// <summary>Done was pressed and everything typed is saved: the host closes the window or the page.</summary>
    public event EventHandler? DoneRequested;

    /// <summary>
    /// Saves what was typed but not saved yet and asks the host to close. When the engine list cannot be saved
    /// (e.g. two engines with the same name) it stays, on the Engines tab, where the message is shown.
    /// </summary>
    public bool TryDone()
    {
        if (DataContext is PreferencesViewModel preferences && !preferences.SavePending())
        {
            Tabs.SelectedItem = EnginesTab;
            return false;
        }

        DoneRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void OnDone(object? sender, RoutedEventArgs e) => TryDone();
}
