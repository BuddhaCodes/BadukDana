using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class LobbyWindow : Window
{
    public LobbyWindow()
    {
        // Created before InitializeComponent: Escape binds to it once, when the XAML loads.
        CloseCommand = new RelayCommand(Close);
        InitializeComponent();
        this.FindControl<ListBox>("ActiveGamesList")!.DoubleTapped += (_, _) =>
        {
            if (DataContext is LobbyViewModel vm && this.FindControl<ListBox>("ActiveGamesList")!.SelectedItem is ActiveGameItem item)
            {
                vm.OpenGameCommand.Execute(item);
            }
        };
        Opened += async (_, _) =>
        {
            if (DataContext is LobbyViewModel vm)
            {
                await vm.InitializeAsync();
            }
        };
    }

    public IRelayCommand CloseCommand { get; }
}
