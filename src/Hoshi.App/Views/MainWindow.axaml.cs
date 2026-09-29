using Avalonia.Controls;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Design-time constructor.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
