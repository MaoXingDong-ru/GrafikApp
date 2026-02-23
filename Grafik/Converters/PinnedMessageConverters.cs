using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Grafik.Converters;

/// <summary>
/// Конвертер для цвета фона закреплённого сообщения
/// </summary>
public class PinnedBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPinned)
        {
            return isPinned ? Color.FromArgb("#FFFDE7") : Color.FromArgb("#E8F5E9");
        }
        return Color.FromArgb("#E8F5E9");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертер для цвета кнопки закрепления
/// </summary>
public class PinButtonColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPinned)
        {
            return isPinned ? Color.FromArgb("#E91E63") : Color.FromArgb("#9C27B0");
        }
        return Color.FromArgb("#9C27B0");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Конвертер Base64-строки в ImageSource для отображения изображений в чате
/// </summary>
public class Base64ToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string base64String && !string.IsNullOrEmpty(base64String))
        {
            try
            {
                var bytes = System.Convert.FromBase64String(base64String);
                return ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
