using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Hoshi.App.ViewModels;
using Hoshi.Core;

namespace Hoshi.App.Views;

/// <summary>View-only plumbing: taps become photo pixel coordinates; the marks are drawn on a canvas.</summary>
public partial class BoardPhotoView : UserControl
{
    public BoardPhotoView()
    {
        AvaloniaXamlLoader.Load(this);
        Panel surface = this.FindControl<Panel>("PhotoSurface")!;
        surface.PointerPressed += (_, e) =>
        {
            if (DataContext is BoardPhotoViewModel vm)
            {
                // The surface is the photo's own size inside the Viewbox: the position is already in its pixels.
                Avalonia.Point p = e.GetPosition(surface);
                vm.Tap(p.X, p.Y);
                e.Handled = true;
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BoardPhotoViewModel vm)
            {
                vm.Marks.CollectionChanged += (_, _) => Redraw(vm);
                Redraw(vm);
            }
        };
    }

    private void Redraw(BoardPhotoViewModel vm)
    {
        Canvas layer = this.FindControl<Canvas>("MarkLayer")!;
        layer.Children.Clear();
        double r = Math.Max(6, Math.Min(vm.PixelWidth, vm.PixelHeight) / 90.0);
        foreach (PhotoMark mark in vm.Marks)
        {
            bool corner = mark.Label.Length > 0;
            var dot = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = corner ? Brushes.Gold : mark.Stone == Stone.Black ? Brushes.Black : Brushes.White,
                Stroke = corner ? Brushes.Black : Brushes.Red,
                StrokeThickness = Math.Max(1.5, r / 4),
            };
            Canvas.SetLeft(dot, mark.X - r);
            Canvas.SetTop(dot, mark.Y - r);
            layer.Children.Add(dot);
        }
    }
}
