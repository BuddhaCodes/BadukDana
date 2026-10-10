using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class ReplaysWindow : Window
{
    public ReplaysWindow()
    {
        // Created before InitializeComponent: Escape binds to it once, when the XAML loads.
        CloseCommand = new RelayCommand(Close);
        InitializeComponent();
        this.FindControl<ListBox>("ReplayList")!.DoubleTapped += (_, _) =>
        {
            if (DataContext is ReplaysViewModel vm)
            {
                vm.OpenCommand.Execute(null);
            }
        };
    }

    public IRelayCommand CloseCommand { get; }
}
