using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DesktopAssistant.UI.Converters;

/// <summary>
/// Converter to check if a number is greater than a threshold value
/// </summary>
public class IntGreaterThanConverter : IValueConverter
{
    public int Threshold { get; set; } = 1;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > Threshold;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
