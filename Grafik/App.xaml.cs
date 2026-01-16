using Grafik.Services;
using System.Diagnostics;

namespace Grafik;

public partial class App : Application
{
    private static bool _backgroundServiceStarted = false;
    public static bool IsAppInForeground { get; private set; } = false;

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
            
            // ✅ Возобновляем полинг
            BackgroundMessageService.Instance.Resume();
        };

        window.Stopped += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Stopped - приложение свернулось");
            IsAppInForeground = false;
            
            // ⏸️ Приостанавливаем полинг чтобы не тратить трафик и батарею
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

        // Получаем текущее имя пользователя
        var currentUserName = Preferences.Get("SelectedEmployee", string.Empty);

        // Не показываем уведомление о собственных сообщениях
        if (e.Message.Sender == currentUserName)
            return;

        // ✅ ПОКАЗЫВАЕМ УВЕДОМЛЕНИЕ ТОЛЬКО ЕСЛИ ПРИЛОЖЕНИЕ ОТКРЫТО
        if (!IsAppInForeground)
        {
            Debug.WriteLine($"[App] Приложение в фоне - уведомление о чате не показано");
            return;
        }

        try
        {
            // Показываем СИСТЕМНОЕ push-уведомление (в трее)
            string messagePreview = e.Message.Type == "file"
                ? $"📎 {e.Message.FileName}"
                : (e.Message.Text.Length > 50 ? e.Message.Text.Substring(0, 50) + "..." : e.Message.Text);

            // 🔔 СИСТЕМНОЕ уведомление
            NotificationService.ShowInstantNotification(
                $"💬 {e.SenderName}",
                messagePreview,
                NotificationService.CHAT_CHANNEL_ID);

            Debug.WriteLine($"[App] Уведомление показано для {e.SenderName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка при показе уведомления: {ex.Message}");
        }
    }
}