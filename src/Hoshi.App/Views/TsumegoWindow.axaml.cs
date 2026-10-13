using Avalonia.Controls;
using Avalonia.Input;

namespace Hoshi.App.Views;

public partial class TsumegoWindow : Window
{
    public TsumegoWindow()
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
