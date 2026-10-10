using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hoshi.App.Controls;

/// <summary>True when the bound enum value equals the converter parameter (for tool radio buttons).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static EnumEqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Bold for true (the player to move), normal otherwise.</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public static BoolToFontWeightConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.SemiBold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>True when the bound string equals the converter parameter (for style classes).</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public static StringEqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && parameter is string p && s == p;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>1 for true, 0 for false (fades elements in and out with an Opacity transition).</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static BoolToOpacityConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Right for true, left for false (your chat lines on the right).</summary>
public sealed class BoolToAlignmentConverter : IValueConverter
{
    public static BoolToAlignmentConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>A study colour as a brush (the colour pickers of the study panel).</summary>
public sealed class StudyColorBrushConverter : IValueConverter
{
    public static StudyColorBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Hoshi.Sgf.Study.StudyColor c ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(GoBoardControl.StudyColorValue(c)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}
