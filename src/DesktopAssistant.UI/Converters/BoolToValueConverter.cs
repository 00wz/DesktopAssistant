using System.Globalization;
using Avalonia.Data.Converters;

namespace DesktopAssistant.UI.Converters;

/// <summary>
/// Универсальный конвертер bool в произвольное значение.
/// TrueValue и FalseValue задаются в AXAML.
/// </summary>
public class BoolToValueConverter : IValueConverter
{
    /// <summary>
    /// Значение, возвращаемое при true
    /// </summary>
    public object? TrueValue { get; set; }
    
    /// <summary>
    /// Значение, возвращаемое при false
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
