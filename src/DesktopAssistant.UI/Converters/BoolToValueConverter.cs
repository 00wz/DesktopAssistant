using System.Globalization;
using Avalonia.Data.Converters;

namespace DesktopAssistant.UI.Converters;

/// <summary>
/// Universal bool-to-arbitrary-value converter.
/// TrueValue and FalseValue are set in AXAML.
/// </summary>
public class BoolToValueConverter : IValueConverter
{
    /// <summary>
    /// Value returned when the input is true.
    /// </summary>
    public object? TrueValue { get; set; }

    /// <summary>
    /// Value returned when the input is false.
    /// </summary>
    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }
        return FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value?.Equals(TrueValue) == true)
        {
            return true;
        }
        if (value?.Equals(FalseValue) == true)
        {
            return false;
        }
        return false;
    }
}
