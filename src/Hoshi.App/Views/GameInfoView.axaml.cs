using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hoshi.App.Views;

/// <summary>A dialog's content, in a window on the desktop or a page on the phone: OK or Cancel completes it.</summary>
public partial class GameInfoView : UserControl
{
    public GameInfoView()
    {
        InitializeComponent();
    }

    /// <summary>True for OK, false for Cancel.</summary>
    public event EventHandler<bool>? Completed;

    private void OnOk(object? sender, RoutedEventArgs e) => Completed?.Invoke(this, true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Completed?.Invoke(this, false);
}
