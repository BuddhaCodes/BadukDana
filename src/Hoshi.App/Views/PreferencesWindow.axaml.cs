using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Hoshi.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        // Created before InitializeComponent: Escape binds to it once, when the XAML loads.
        CloseCommand = new RelayCommand(() => View.TryDone());
        InitializeComponent();
        View.DoneRequested += (_, _) => Close();
    }

    /// <summary>Done or Escape: saves what was typed but not saved yet, then closes.</summary>
    public IRelayCommand CloseCommand { get; }
}
