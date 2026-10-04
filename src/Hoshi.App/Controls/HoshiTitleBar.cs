using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// Window chrome only: no application logic here.
/// </summary>
public sealed class HoshiTitleBar : Border
{
    public const double BarHeight = 36;

    /// <summary>Show the bar even when the platform did not extend the window (screenshots in headless tests).</summary>
    public static bool AlwaysShow { get; set; }

    private readonly TextBlock _title = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12.5,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(10, 0, 0, 0),
    };

    private readonly Image _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private readonly Avalonia.Controls.Shapes.Path _maximizeGlyph;
    private Window? _window;

    public HoshiTitleBar()
    {
        Height = BarHeight;
        IsVisible = false;
        Bind(BackgroundProperty, this.GetResourceObservable("Bg.Bar"));
        _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable("Text.Secondary"));

        _minimize = CaptionButton("Minimize", Geometry.Parse("M0,5 L10,5"), out _);
        _maximize = CaptionButton("Maximize", Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z"), out _maximizeGlyph);
        _close = CaptionButton("Close", Geometry.Parse("M0,0 L10,10 M10,0 L0,10"), out _);
        _close.Classes.Add("close");
        _minimize.Click += (_, _) => { if (_window is { } w) { w.WindowState = WindowState.Minimized; } };
        _maximize.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => _window?.Close();
        _buttons.Children.Add(_minimize);
        _buttons.Children.Add(_maximize);
        _buttons.Children.Add(_close);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        left.Children.Add(_icon);
        left.Children.Add(_title);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(_buttons, 1);
        grid.Children.Add(_buttons);
        Child = grid;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
        {
            return;
        }

        bool mac = OperatingSystem.IsMacOS();
        _window.ExtendClientAreaToDecorationsHint = true;
        _window.ExtendClientAreaTitleBarHeightHint = BarHeight;
        _window.ExtendClientAreaChromeHints = mac ? ExtendClientAreaChromeHints.PreferSystemChrome : ExtendClientAreaChromeHints.NoChrome;
        _buttons.IsVisible = !mac;
        if (mac)
        {
            ((Control)((Grid)Child!).Children[0]).Margin = new Thickness(78, 0, 0, 0); // room for the traffic lights
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_window is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            _window.BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.TitleProperty || e.Property == Window.IconProperty || e.Property == Window.WindowStateProperty
            || e.Property == Window.IsExtendedIntoWindowDecorationsProperty || e.Property == Window.CanResizeProperty
            || e.Property == WindowBase.OwnerProperty)
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

        IsVisible = AlwaysShow || _window.IsExtendedIntoWindowDecorations;
        _title.Text = _window.Title;
        _icon.Source ??= Themes.Skins.Image("avares://Hoshi/Assets/hoshi.png");
        _maximize.IsVisible = _window.CanResize;
        _minimize.IsVisible = _window.Owner is null; // dialogs close; only top-level windows minimise
        _maximizeGlyph.Data = _window.WindowState == WindowState.Maximized
            ? Geometry.Parse("M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z")
            : Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");
        Avalonia.Automation.AutomationProperties.SetName(_maximize, _window.WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private void ToggleMaximize()
    {
        if (_window is { CanResize: true } w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private static Button CaptionButton(string name, Geometry glyph, out Avalonia.Controls.Shapes.Path path)
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
            Height = BarHeight,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Focusable = false,
        };
        button.Classes.Add("caption");
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
}
