using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

        // View-only gestures; what they do is up to the view model.
        // The interface size scales the content: the layout works in the scaled size.
        SizeChanged += (_, e) => ShellModel?.UpdateSize(e.NewSize.Width / Scale, e.NewSize.Height / Scale);
        DataContextChanged += (_, _) => ShellModel?.UpdateSize(Bounds.Width / Scale, Bounds.Height / Scale);
        PhoneSheetHandle.PointerPressed += OnHandlePressed;
        PhoneSheetHandle.PointerMoved += OnHandleMoved;
        PhoneSheetHandle.PointerReleased += OnHandleReleased;
        PhoneSheetHandle.PointerCaptureLost += (_, _) => EndHandleDrag();
        PhoneTop.PointerPressed += OnTopPressed;
        PhoneTop.PointerMoved += OnTopMoved;
        PhoneTop.PointerReleased += (_, _) => _swipeFrom = null;
        PhoneTop.PointerCaptureLost += (_, _) => _swipeFrom = null;
    }

    /// <summary>Horizontal distance a finger travels on the header for one move back or forward.</summary>
    public const double SwipeStep = 28;

    private double? _dragStartY;
    private double _dragStartHeight;
    private double? _swipeFrom;

    private PhoneShellViewModel? ShellModel => DataContext as PhoneShellViewModel;

    private double Scale => Math.Max(1, ShellModel?.Main.UiScale ?? 1);

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ShellModel is not { } shell)
        {
            return;
        }

        _dragStartY = e.GetPosition(this).Y;
        _dragStartHeight = shell.SheetHeight;
        e.Pointer.Capture(PhoneSheetHandle);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartY is { } start && ShellModel is { } shell)
        {
            shell.DragSheet(_dragStartHeight + ((start - e.GetPosition(this).Y) / Scale), Bounds.Height / Scale);
        }
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndHandleDrag();
        e.Pointer.Capture(null);
    }

    private void EndHandleDrag()
    {
        if (_dragStartY is not null)
        {
            _dragStartY = null;
            ShellModel?.EndSheetDrag(_dragStartHeight);
        }
    }

    // Sliding a finger sideways on the header steps through the game: right goes forward, left goes back.
    private void OnTopPressed(object? sender, PointerPressedEventArgs e)
    {
        _swipeFrom = e.GetPosition(this).X;
    }

    private void OnTopMoved(object? sender, PointerEventArgs e)
    {
        if (_swipeFrom is not { } from || ShellModel is not { } shell)
        {
            return;
        }

        double x = e.GetPosition(this).X;
        int steps = (int)((x - from) / SwipeStep);
        if (steps == 0)
        {
            return;
        }

        e.Pointer.Capture(PhoneTop);
        _swipeFrom = from + (steps * SwipeStep);
        shell.Step(steps);
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
