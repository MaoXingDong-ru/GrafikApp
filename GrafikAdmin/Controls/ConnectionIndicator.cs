using System.Diagnostics;
using GrafikAdmin.Services;

namespace GrafikAdmin.Controls;

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

        // Начальное состояние — серый (неизвестно)
        BackgroundColor = Colors.Gray;

        Debug.WriteLine("[ConnectionIndicator] Создан");

        // Подписываемся на изменения статуса
        FirebaseConnectionMonitor.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Устанавливаем текущий статус
        UpdateIndicator(FirebaseConnectionMonitor.Instance.IsConnected);
        
        // Принудительно запускаем проверку
        _ = FirebaseConnectionMonitor.Instance.CheckConnectionAsync();
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        Debug.WriteLine($"[ConnectionIndicator] Получено событие: {isConnected}");
        UpdateIndicator(isConnected);
    }

    private void UpdateIndicator(bool isConnected)
    {
        Debug.WriteLine($"[ConnectionIndicator] UpdateIndicator: {isConnected}");
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BackgroundColor = isConnected
                ? Color.FromArgb("#4CAF50")  // Зелёный
                : Color.FromArgb("#F44336"); // Красный
                
            Debug.WriteLine($"[ConnectionIndicator] Цвет установлен: {(isConnected ? "зелёный" : "красный")}");
        });
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler == null)
        {
            Debug.WriteLine("[ConnectionIndicator] Отписка от событий");
            FirebaseConnectionMonitor.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }
}