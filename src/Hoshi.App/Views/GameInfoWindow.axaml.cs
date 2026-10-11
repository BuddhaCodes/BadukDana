using Avalonia.Controls;

namespace Hoshi.App.Views;

public partial class GameInfoWindow : Window
{
    public GameInfoWindow()
    {
        InitializeComponent();
        View.Completed += (_, ok) => Close(ok);
    }
}
