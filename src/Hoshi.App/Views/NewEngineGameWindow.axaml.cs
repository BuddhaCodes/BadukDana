using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hoshi.App.Views;

public partial class NewEngineGameWindow : Window
{
    public NewEngineGameWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
