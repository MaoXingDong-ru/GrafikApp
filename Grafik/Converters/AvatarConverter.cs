using System.Globalization;
using Microsoft.Maui.Controls;

namespace Grafik.Converters;

/// <summary>
/// Генерирует инициалы из полного имени (Фамилия Имя -> ФИ)
/// </summary>
public class NameToInitialsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                // Берём первые буквы фамилии и имени
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            else if (parts.Length == 1)
            {
                // Если только одно слово, берём первую букву
                return parts[0][0].ToString().ToUpper();
            }
        }

        return "??";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Генерирует цвет аватарки на основе имени (стабильный hash)
/// </summary>
public class NameToColorConverter : IValueConverter
{
    private static readonly Color[] AvatarColors = new[]
    {
        Color.FromArgb("#E57373"), // Красный
        Color.FromArgb("#F06292"), // Розовый
        Color.FromArgb("#BA68C8"), // Фиолетовый
        Color.FromArgb("#9575CD"), // Тёмно-фиолетовый
        Color.FromArgb("#7986CB"), // Индиго
        Color.FromArgb("#64B5F6"), // Синий
        Color.FromArgb("#4FC3F7"), // Голубой
        Color.FromArgb("#4DD0E1"), // Циан
        Color.FromArgb("#4DB6AC"), // Бирюзовый
        Color.FromArgb("#81C784"), // Зелёный
        Color.FromArgb("#AED581"), // Салатовый
        Color.FromArgb("#FF8A65"), // Оранжевый
        Color.FromArgb("#A1887F"), // Коричневый
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            // Генерируем стабильный индекс на основе имени
            int hash = name.GetHashCode();
            int index = Math.Abs(hash) % AvatarColors.Length;
            return AvatarColors[index];
        }

        return Color.FromArgb("#BDBDBD"); // Серый по умолчанию
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
