using System;
using System.Diagnostics;
using Grafik.Services;

namespace Grafik.Controls;

/// <summary>
/// Индикатор соединения с Firebase (кружок)
/// </summary>
public class ConnectionIndicator : Frame
{
    public ConnectionIndicator()
    {
        WidthRequest = 12;
        HeightRequest = 12;
        CornerRadius = 6;
        Padding = 0;
        Margin = new Thickness(10, 0);
        HasShadow = false;
        BorderColor = Colors.Transparent;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Center;

        // Начальное состояние — серый
        BackgroundColor = Colors.Gray;

        // Подписываемся на изменения статуса
        FirebaseConnectionMonitor.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Устанавливаем текущий статус
        UpdateIndicator(FirebaseConnectionMonitor.Instance.IsConnected);
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        Debug.WriteLine($"[ConnectionIndicator] Событие: {isConnected}");
        UpdateIndicator(isConnected);
    }

    private void UpdateIndicator(bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BackgroundColor = isConnected
                ? Color.FromArgb("#4CAF50")  // Зелёный
                : Color.FromArgb("#F44336"); // Красный
        });
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler == null)
        {
            FirebaseConnectionMonitor.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }
}
