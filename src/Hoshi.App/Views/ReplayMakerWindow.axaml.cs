using Avalonia.Controls;
using Avalonia.Input;

namespace Hoshi.App.Views;

public partial class ReplayMakerWindow : Window
{
    public ReplayMakerWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
