using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace Grafik.Converters;

/// <summary>
/// Конвертирует текст сообщения в FormattedString, где URL-ссылки кликабельны
/// и открываются во внешнем браузере.
/// </summary>
public partial class TextToFormattedStringConverter : IValueConverter
{
    // Регулярное выражение для поиска URL в тексте
    [GeneratedRegex(@"(https?://[^\s<>""')\]]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return new FormattedString();

        var formattedString = new FormattedString();
        var matches = UrlRegex().Matches(text);

        int lastIndex = 0;

        foreach (Match match in matches)
        {
            // Добавляем обычный текст перед ссылкой
            if (match.Index > lastIndex)
            {
                formattedString.Spans.Add(new Span
                {
                    Text = text[lastIndex..match.Index],
                    FontSize = 14
                });
            }

            // Добавляем кликабельную ссылку
            var urlSpan = new Span
            {
                Text = match.Value,
                TextColor = Color.FromArgb("#1565C0"),
                TextDecorations = TextDecorations.Underline,
                FontSize = 14
            };

            var url = match.Value;
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                try
                {
                    await Launcher.OpenAsync(new Uri(url));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LinkConverter] Ошибка открытия URL: {ex.Message}");
                }
            };
            urlSpan.GestureRecognizers.Add(tapGesture);

            formattedString.Spans.Add(urlSpan);

            lastIndex = match.Index + match.Length;
        }

        // Добавляем оставшийся текст после последней ссылки
        if (lastIndex < text.Length)
        {
            formattedString.Spans.Add(new Span
            {
                Text = text[lastIndex..],
                FontSize = 14
            });
        }

        return formattedString;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
