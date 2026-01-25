using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DesktopAssistant.UI.Converters;

/// <summary>
/// Конвертер bool в цвет текста для сообщений пользователя
/// </summary>
public class UserMessageForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUser && isUser)
        {
            return Brushes.White;
        }
        return Brushes.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертер bool в цвет фона для сообщений
/// </summary>
public class UserMessageBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUser && isUser)
        {
            return new SolidColorBrush(Color.Parse("#007ACC"));
        }
        return new SolidColorBrush(Color.Parse("#E8E8E8"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертер bool в HorizontalAlignment для сообщений
/// </summary>
public class UserMessageAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUser && isUser)
        {
            return Avalonia.Layout.HorizontalAlignment.Right;
        }
        return Avalonia.Layout.HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
