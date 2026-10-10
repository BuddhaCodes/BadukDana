using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        // Created before InitializeComponent: the Done button and Escape bind to it once, when the XAML loads.
        CloseCommand = new RelayCommand(Done);
        InitializeComponent();
    }

    /// <summary>Done or Escape: saves what was typed but not saved yet, then closes.</summary>
    public IRelayCommand CloseCommand { get; }

    private void Done()
    {
        if (DataContext is PreferencesViewModel preferences && !preferences.SavePending())
        {
            // The engine list has a problem (e.g. two engines with the same name): show its message.
            this.FindControl<TabControl>("Tabs")!.SelectedItem = this.FindControl<TabItem>("EnginesTab");
            return;
        }

        Close();
    }
}
