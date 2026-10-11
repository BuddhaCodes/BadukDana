using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Hoshi.App.Views;

public partial class ReplaysWindow : Window
{
    public ReplaysWindow()
    {
        // Created before InitializeComponent: Escape binds to it once, when the XAML loads.
        CloseCommand = new RelayCommand(Close);
        InitializeComponent();
    }

    public IRelayCommand CloseCommand { get; }
}
