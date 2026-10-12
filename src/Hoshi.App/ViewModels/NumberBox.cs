using System.Globalization;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Values typed in number boxes (<c>NumericUpDown</c>, which gives null when emptied). An empty or out-of-range box
/// throws with a message in the user's language; the binding shows it under the box and the last good value stays
/// (instead of Avalonia's raw "Could not convert … to System.Int32").
/// </summary>
public static class NumberBox
{
    public static int Whole(decimal? value, int min, int max)
    {
        if (value is not { } v || v < min || v > max || v != decimal.Truncate(v))
        {
            throw new ArgumentException(Tr.F("Common.WholeNumberRange", min.ToString(CultureInfo.CurrentCulture), max.ToString(CultureInfo.CurrentCulture)));
        }

        return (int)v;
    }

    public static double Number(decimal? value, double min, double max)
    {
        if (value is not { } v || (double)v < min || (double)v > max)
        {
            throw new ArgumentException(Tr.F("Common.NumberRange", min.ToString(CultureInfo.CurrentCulture), max.ToString(CultureInfo.CurrentCulture)));
        }

        return (double)v;
    }
}
