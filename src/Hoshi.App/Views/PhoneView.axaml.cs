using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

/// <summary>The phone layout (Android). Android's back button closes a message, a page, a sheet, then a quiz.</summary>
public partial class PhoneView : UserControl
{
    private TopLevel? _top;

    public PhoneView()
    {
        InitializeComponent();

        // Choosing something in the menu closes it.
        PhoneMenuSheet.AddHandler(Button.ClickEvent, (_, _) => (DataContext as PhoneShellViewModel)?.CloseMenu(), RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top is not null)
        {
            _top.BackRequested += OnBackRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null)
        {
            _top.BackRequested -= OnBackRequested;
            _top = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PhoneShellViewModel shell && shell.Back())
        {
            e.Handled = true;
        }
    }
}
