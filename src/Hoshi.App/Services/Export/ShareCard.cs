using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;

namespace Hoshi.App.Services.Export;

/// <summary>The shapes of a share card: square (Instagram), 16:9 (X/Twitter, Discord) and vertical (stories).</summary>
public enum CardFormat
{
    Square,
    Wide,
    Story,
}

/// <summary>The words on a card; empty lines are left out.</summary>
public sealed record CardText(string Title, string Subtitle, string Verdict, string Note, bool Brand);

/// <summary>
/// A picture to share a moment: the board with the players, the move, KataGo's verdict and the study note, laid out
/// for each <see cref="CardFormat"/>. Drawn off screen with Avalonia; must run on the UI thread.
/// </summary>
public static class ShareCard
{
    public static PixelSize Size(CardFormat format) => format switch
    {
        CardFormat.Wide => new PixelSize(1600, 900),
        CardFormat.Story => new PixelSize(1080, 1920),
        _ => new PixelSize(1080, 1080),
    };

    public static RenderTargetBitmap Render(BoardShot shot, CardText text, CardFormat format, bool coordinates, BoardStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(text);
        PixelSize size = Size(format);
        int boardPixels = format switch
        {
            CardFormat.Wide => 820,
            CardFormat.Story => 1000,
            _ => 780,
        };

        RenderTargetBitmap board = BoardPicture.Render(shot, boardPixels, coordinates, style);
        var image = new Image { Source = board, Width = boardPixels, Height = boardPixels, Stretch = Stretch.Uniform };
        var words = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        double scale = format == CardFormat.Square ? 0.85 : 1;
        Add(words, text.Title, 52 * scale, FontWeight.Bold, Brushes.White);
        Add(words, text.Subtitle, 36 * scale, FontWeight.Normal, new SolidColorBrush(Color.Parse("#C9CCD3")));
        Add(words, text.Verdict, 36 * scale, FontWeight.SemiBold, new SolidColorBrush(Color.Parse("#F5B041")));
        Add(words, text.Note, 32 * scale, FontWeight.Normal, new SolidColorBrush(Color.Parse("#E8E6E3")), maxLines: format == CardFormat.Story ? 8 : 4);
        if (text.Brand)
        {
            Add(words, "Hoshi", 26 * scale, FontWeight.SemiBold, new SolidColorBrush(Color.Parse("#8A8F99")));
        }

        Control layout = format switch
        {
            CardFormat.Wide => new DockPanel
            {
                Margin = new Thickness(40),
                Children = { Dock(image, Avalonia.Controls.Dock.Left), Pad(words, new Thickness(48, 0, 0, 0)) },
            },
            CardFormat.Story => new DockPanel
            {
                Margin = new Thickness(40, 120, 40, 80),
                Children = { Dock(image, Avalonia.Controls.Dock.Top), Pad(words, new Thickness(0, 60, 0, 0)) },
            },
            _ => new DockPanel
            {
                Margin = new Thickness(40, 36, 40, 30),
                Children = { Dock(image, Avalonia.Controls.Dock.Top), Pad(words, new Thickness(10, 24, 10, 0)) },
            },
        };

        var card = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#1D1F24"), 0), new GradientStop(Color.Parse("#2E3138"), 1) },
            },
            Child = layout,
        };
        card.Measure(new Avalonia.Size(size.Width, size.Height));
        card.Arrange(new Rect(0, 0, size.Width, size.Height));
        var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(card);
        return bitmap;
    }

    /// <summary>A text card of numbers (the month or year summary): a title, a subtitle and tiles of number + label.</summary>
    public static RenderTargetBitmap RenderStats(string title, string subtitle, IReadOnlyList<(string Number, string Label)> tiles, CardFormat format, bool brand)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        PixelSize size = Size(format);
        int columns = format == CardFormat.Wide ? 3 : 2;
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(0, 40, 0, 0) };
        foreach ((string number, string label) in tiles)
        {
            var tile = new StackPanel { Margin = new Thickness(0, 0, 24, 36) };
            tile.Children.Add(new TextBlock { Text = number, FontSize = 88, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#F5B041")) });
            tile.Children.Add(new TextBlock { Text = label, FontSize = 32, Foreground = new SolidColorBrush(Color.Parse("#C9CCD3")), TextWrapping = TextWrapping.Wrap, MaxLines = 3 });
            grid.Children.Add(tile);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Add(words, title, 84, FontWeight.Bold, Brushes.White);
        Add(words, subtitle, 40, FontWeight.Normal, new SolidColorBrush(Color.Parse("#C9CCD3")));
        words.Children.Add(grid);
        if (brand)
        {
            Add(words, "Hoshi", 30, FontWeight.SemiBold, new SolidColorBrush(Color.Parse("#8A8F99")));
        }

        var card = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Padding = new Thickness(format == CardFormat.Wide ? 90 : 80),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#1D1F24"), 0), new GradientStop(Color.Parse("#3A2F22"), 1) },
            },
            Child = words,
        };
        card.Measure(new Avalonia.Size(size.Width, size.Height));
        card.Arrange(new Rect(0, 0, size.Width, size.Height));
        var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(card);
        return bitmap;
    }

    private static void Add(StackPanel panel, string text, double fontSize, FontWeight weight, IBrush brush, int maxLines = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = maxLines,
        });
    }

    private static Control Dock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.HorizontalAlignment = HorizontalAlignment.Center;
        return control;
    }

    private static Control Pad(Control control, Thickness margin)
    {
        control.Margin = margin;
        return control;
    }
}
