using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Grafik.Converters
{
    /// <summary>
    /// IsMine=true → End (справа), false → Start (слева)
    /// </summary>
    public class BoolToAlignmentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isMine)
                return isMine ? LayoutOptions.End : LayoutOptions.Start;
            return LayoutOptions.Start;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// IsMine=true → отступ справа маленький, слева большой (прижимаем вправо)
    /// IsMine=false → наоборот (прижимаем влево)
    /// </summary>
    public class BoolToBubbleMarginConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isMine)
                return isMine ? new Thickness(60, 2, 8, 2) : new Thickness(8, 2, 60, 2);
            return new Thickness(8, 2, 60, 2);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// IsMine=true → чуть более тёмный фон (свои сообщения)
    /// IsMine=false → ещё чуть тёмнее (чужие сообщения)
    /// Оба цвета чуть более насыщенные для лучшей читабельности на ПК
    /// </summary>
    public class BoolToBubbleBackgroundConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isMine)
                return isMine ? Color.FromArgb("#D0E6FF") : Color.FromArgb("#EDEDED");
            return Color.FromArgb("#EDEDED");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Инвертирует bool
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}