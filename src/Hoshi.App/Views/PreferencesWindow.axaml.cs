using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Hoshi.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        CloseCommand = new RelayCommand(Close);
    }

    public IRelayCommand CloseCommand { get; }
}
