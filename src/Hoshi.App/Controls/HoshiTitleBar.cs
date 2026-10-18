using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace Hoshi.App.Controls;

/// <summary>
/// Hoshi's own window title bar (part of every window's template, see Styles/Base.axaml). It asks the window to
/// draw into the system title bar area; where the platform agrees it shows the icon, the title and its own
/// minimise / maximise / close buttons in the theme's colours. On macOS the native "traffic lights" stay, with
/// room left for them. Where the platform keeps its own title bar (some Linux window managers), this hides itself.
/// <para>
/// Windows without decorations (the splash) get no bar at all. A window whose own header already is the title bar
/// (the main window, redesign 2026-10-18) sets <see cref="IsMergedProperty"/>: the window still draws into the
/// title bar area, but this bar hides; the header carries <see cref="CaptionButtons"/> and is a
/// <see cref="IsDragAreaProperty">drag area</see>.
/// </para>
/// Window chrome only: no application logic here.
/// </summary>
public sealed class HoshiTitleBar : Border
{
    public const double BarHeight = 36;

    /// <summary>Show the bar even when the platform did not extend the window (screenshots in headless tests).</summary>
    public static bool AlwaysShow { get; set; }

    /// <summary>The window's own header is its title bar: this bar hides (set on the window).</summary>
    public static readonly AttachedProperty<bool> IsMergedProperty =
        AvaloniaProperty.RegisterAttached<HoshiTitleBar, Window, bool>("IsMerged");

    /// <summary>Pressing here moves the window and a double click maximises it, like a title bar.</summary>
    public static readonly AttachedProperty<bool> IsDragAreaProperty =
        AvaloniaProperty.RegisterAttached<HoshiTitleBar, Control, bool>("IsDragArea");

    public static bool GetIsMerged(Window window) => window.GetValue(IsMergedProperty);

    public static void SetIsMerged(Window window, bool value) => window.SetValue(IsMergedProperty, value);

    public static bool GetIsDragArea(Control control) => control.GetValue(IsDragAreaProperty);

    public static void SetIsDragArea(Control control, bool value) => control.SetValue(IsDragAreaProperty, value);

    /// <summary>macOS keeps its traffic lights at the top left: headers leave this much room for them.</summary>
    public static double TrafficLightsRoom => OperatingSystem.IsMacOS() ? 78 : 0;

    static HoshiTitleBar()
    {
        IsDragAreaProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.PointerPressed -= OnDragAreaPressed;
            if (e.NewValue is true)
            {
                control.PointerPressed += OnDragAreaPressed;
            }
        });
    }

    private readonly TextBlock _title = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12.5,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(10, 0, 0, 0),
    };

    private readonly Image _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly CaptionButtons _buttons = new() { HorizontalAlignment = HorizontalAlignment.Right, ButtonHeight = BarHeight };
    private Window? _window;

    public HoshiTitleBar()
    {
        Height = BarHeight;
        IsVisible = false;
        Bind(BackgroundProperty, this.GetResourceObservable("Bg.Bar"));
        _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable("Text.Secondary"));

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12 + TrafficLightsRoom, 0, 0, 0), IsHitTestVisible = false };
        left.Children.Add(_icon);
        left.Children.Add(_title);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(_buttons, 1);
        grid.Children.Add(_buttons);
        Child = grid;
        SetIsDragArea(this, true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
        {
            return;
        }

        // A window without decorations (the splash) draws its own round card: nothing to extend into.
        if (_window.SystemDecorations != SystemDecorations.None)
        {
            _window.ExtendClientAreaToDecorationsHint = true;
            _window.ExtendClientAreaTitleBarHeightHint = BarHeight;
            _window.ExtendClientAreaChromeHints = OperatingSystem.IsMacOS() ? ExtendClientAreaChromeHints.PreferSystemChrome : ExtendClientAreaChromeHints.NoChrome;
        }

        _window.PropertyChanged += OnWindowPropertyChanged;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.TitleProperty || e.Property == Window.IsExtendedIntoWindowDecorationsProperty
            || e.Property == IsMergedProperty || e.Property == Window.SystemDecorationsProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        if (_window is null)
        {
            return;
        }

        bool undecorated = _window.SystemDecorations == SystemDecorations.None;
        IsVisible = !undecorated && !GetIsMerged(_window) && (AlwaysShow || _window.IsExtendedIntoWindowDecorations);
        _title.Text = _window.Title;
        _icon.Source ??= Themes.Skins.Image("avares://Hoshi.App/Assets/hoshi.png");
    }

    private static void OnDragAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        // Buttons, tabs and sliders handle their own presses; only the bare header moves the window.
        if (e.Handled || sender is not Control control || TopLevel.GetTopLevel(control) is not Window window
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
        }
        else
        {
            window.BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    internal static void ToggleMaximize(Window window)
    {
        if (window.CanResize)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}

/// <summary>
/// Minimise / maximise / close in the theme's colours, for a window drawn into its title bar area: inside
/// <see cref="HoshiTitleBar"/>, or at the right end of a header that is the title bar. Hidden on macOS (the native
/// traffic lights stay) and where the platform keeps its own title bar.
/// </summary>
public sealed class CaptionButtons : StackPanel
{
    public static readonly StyledProperty<double> ButtonHeightProperty =
        AvaloniaProperty.Register<CaptionButtons, double>(nameof(ButtonHeight), HoshiTitleBar.BarHeight);

    private static readonly Geometry Maximized = Geometry.Parse("M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z");
    private static readonly Geometry Normal = Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");

    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private readonly Avalonia.Controls.Shapes.Path _maximizeGlyph;
    private Window? _window;

    public double ButtonHeight
    {
        get => GetValue(ButtonHeightProperty);
        set => SetValue(ButtonHeightProperty, value);
    }

    public CaptionButtons()
    {
        Orientation = Orientation.Horizontal;
        IsVisible = false;
        _minimize = CaptionButton("Minimize", Geometry.Parse("M0,5 L10,5"), out _);
        _maximize = CaptionButton("Maximize", Normal, out _maximizeGlyph);
        _close = CaptionButton("Close", Geometry.Parse("M0,0 L10,10 M10,0 L0,10"), out _);
        _close.Classes.Add("close");
        _minimize.Click += (_, _) => { if (_window is { } w) { w.WindowState = WindowState.Minimized; } };
        _maximize.Click += (_, _) => { if (_window is { } w) { HoshiTitleBar.ToggleMaximize(w); } };
        _close.Click += (_, _) => _window?.Close();
        Children.Add(_minimize);
        Children.Add(_maximize);
        Children.Add(_close);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ButtonHeightProperty)
        {
            foreach (Button b in new[] { _minimize, _maximize, _close })
            {
                b.Height = ButtonHeight;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
            Update();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.IsExtendedIntoWindowDecorationsProperty
            || e.Property == Window.CanResizeProperty || e.Property == WindowBase.OwnerProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        if (_window is null)
        {
            return;
        }

        IsVisible = !OperatingSystem.IsMacOS() && (HoshiTitleBar.AlwaysShow || _window.IsExtendedIntoWindowDecorations);
        _maximize.IsVisible = _window.CanResize;
        _minimize.IsVisible = _window.Owner is null; // dialogs close; only top-level windows minimise
        bool maximized = _window.WindowState == WindowState.Maximized;
        _maximizeGlyph.Data = maximized ? Maximized : Normal;
        Avalonia.Automation.AutomationProperties.SetName(_maximize, maximized ? "Restore" : "Maximize");
    }

    private Button CaptionButton(string name, Geometry glyph, out Avalonia.Controls.Shapes.Path path)
    {
        path = new Avalonia.Controls.Shapes.Path
        {
            Data = glyph,
            StrokeThickness = 1,
            Width = 10,
            Height = 10,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, path.GetResourceObservable("Text.Primary"), Avalonia.Data.BindingPriority.Template);
        var button = new Button
        {
            Content = path,
            Width = 46,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Focusable = false,
        };
        button.Classes.Add("caption");
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
}
