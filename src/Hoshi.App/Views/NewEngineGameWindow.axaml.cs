using Avalonia.Controls;

namespace Hoshi.App.Views;

public partial class NewEngineGameWindow : Window
{
    public NewEngineGameWindow()
    {
        InitializeComponent();
        View.Completed += (_, ok) => Close(ok);
    }
}
