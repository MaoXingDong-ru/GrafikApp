using Grafik.Services;
using System.Diagnostics;

namespace Grafik;

public partial class App : Application
{
    private static bool _backgroundServiceStarted = false;
    public static bool IsAppInForeground { get; private set; } = false;

    /// <summary>
    /// Флаг: открыт ли сейчас экран чата (уведомления не нужны, пользователь видит сообщения)
    /// </summary>
    public static bool IsChatPageActive { get; set; } = false;

    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();

        // Запускаем фоновый сервис при старте приложения
        StartBackgroundService();
    }

    private void StartBackgroundService()
    {
        if (_backgroundServiceStarted)
            return;

        try
        {
            Debug.WriteLine("[App] Инициализация фонового сервиса сообщений");

            var firebaseUrl = Preferences.Get("FirebaseUrl",
                "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");

            // Запускаем сервис
            BackgroundMessageService.Instance.Start(firebaseUrl);
            
            // Подписываемся на события
            BackgroundMessageService.Instance.NewMessageReceived += OnNewMessageReceived;

            _backgroundServiceStarted = true;

            Debug.WriteLine("[App] Фоновый сервис успешно запущен");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка при запуске фонового сервиса: {ex.Message}");
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Resumed += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Resumed - приложение активировалось");
            IsAppInForeground = true;
            
            // ✅ Возобновляем частый полинг
            BackgroundMessageService.Instance.Resume();
        };

        window.Stopped += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Stopped - приложение свернулось");
            IsAppInForeground = false;
            
            // ⏸️ Переключаемся на редкий полинг (но НЕ останавливаем!)
            BackgroundMessageService.Instance.Pause();
        };

        window.Destroying += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Destroying - приложение закрывается");
            
            try
            {
                BackgroundMessageService.Instance.NewMessageReceived -= OnNewMessageReceived;
                BackgroundMessageService.Instance.Stop();
                _backgroundServiceStarted = false;
                Debug.WriteLine("[App] Фоновый сервис остановлен");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Ошибка при остановке: {ex.Message}");
            }
        };

        return window;
    }

    /// <summary>
    /// Обработчик события получения нового сообщения
    /// </summary>
    private void OnNewMessageReceived(object? sender, BackgroundMessageService.NewMessageEventArgs e)
    {
        Debug.WriteLine($"[App] Новое сообщение от {e.SenderName}: {e.Message.Text}");

        // Не показываем уведомление, если чат открыт и приложение на переднем плане
        if (IsAppInForeground && IsChatPageActive)
        {
            Debug.WriteLine("[App] Чат открыт — уведомление не показываем");
            return;
        }

        try
        {
            // Формируем превью сообщения
            string messagePreview = e.Message.Type switch
            {
                "file" => $"📎 {e.Message.FileName}",
                "image" => $"🖼️ {e.Message.FileName ?? "Изображение"}",
                _ => e.Message.Text.Length > 80
                    ? e.Message.Text[..80] + "..."
                    : e.Message.Text
            };

            // 🔔 СИСТЕМНОЕ уведомление
            NotificationService.ShowInstantNotification(
                $"💬 {e.SenderName}",
                messagePreview,
                NotificationService.CHAT_CHANNEL_ID);

            Debug.WriteLine($"[App] 🔔 Уведомление показано: {e.SenderName} — {messagePreview}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка при показе уведомления: {ex.Message}");
        }
    }
}