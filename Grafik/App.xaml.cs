using Grafik.Services;
using System.Diagnostics;

namespace Grafik;

public partial class App : Application
{
    private static bool _backgroundServiceStarted = false;
    private static bool _monitorInitialized = false;

    public static bool IsAppInForeground { get; private set; } = false;
    public static bool IsChatPageActive { get; set; } = false;

    public App()
    {
        InitializeComponent();
        
        // Принудительно устанавливаем тёмную тему
        UserAppTheme = AppTheme.Dark;
        
        MainPage = new AppShell();

#if !ANDROID
        StartBackgroundService();
#endif
    }

    /// <summary>
    /// Инициализация мониторинга Firebase (один раз)
    /// </summary>
    public static void InitializeFirebaseMonitor()
    {
        if (_monitorInitialized)
            return;

        Debug.WriteLine("[App] Инициализация мониторинга Firebase...");

        var url = Preferences.Get("FirebaseUrl", string.Empty);
        if (string.IsNullOrEmpty(url))
        {
            var defaultUrl = "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app";
            Preferences.Set("FirebaseUrl", defaultUrl);
            Debug.WriteLine($"[App] Установлен дефолтный URL");
        }

        FirebaseConnectionMonitor.Instance.Start();
        _monitorInitialized = true;
        Debug.WriteLine("[App] Мониторинг Firebase запущен ✓");
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

            BackgroundMessageService.Instance.Start(firebaseUrl);
            BackgroundMessageService.Instance.NewMessageReceived += OnNewMessageReceived;

            _backgroundServiceStarted = true;
            Debug.WriteLine("[App] Фоновый сервис успешно запущен");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка при запуске фонового сервиса: {ex.Message}");
        }
    }

    private void StartForegroundPollingService()
    {
#if ANDROID
        try
        {
            Debug.WriteLine("[App] Запуск Foreground Service для полинга сообщений");

            var intent = new Android.Content.Intent(
                Android.App.Application.Context,
                typeof(MessagePollingService));

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                Android.App.Application.Context.StartForegroundService(intent);
            }
            else
            {
                Android.App.Application.Context.StartService(intent);
            }

            Debug.WriteLine("[App] Foreground Service запущен");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ❌ Ошибка запуска Foreground Service: {ex.Message}");
        }
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Created += async (s, e) =>
        {
            Debug.WriteLine("[App] Window.Created");

            // Инициализируем мониторинг соединения
            InitializeFirebaseMonitor();

#if ANDROID
            StartForegroundPollingService();
#endif
        };

        window.Resumed += async (s, e) =>
        {
            Debug.WriteLine("[App] Window.Resumed");
            IsAppInForeground = true;

            FirebaseConnectionMonitor.Instance.Resume();
#if !ANDROID
            BackgroundMessageService.Instance.Resume();
#endif
        };

        window.Stopped += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Stopped");
            IsAppInForeground = false;

            FirebaseConnectionMonitor.Instance.Pause();
        };

        window.Destroying += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Destroying");

#if !ANDROID
            try
            {
                BackgroundMessageService.Instance.NewMessageReceived -= OnNewMessageReceived;
                BackgroundMessageService.Instance.Stop();
                _backgroundServiceStarted = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Ошибка при остановке: {ex.Message}");
            }
#else
            Debug.WriteLine("[App] Foreground Service продолжает работать в фоне");
#endif
        };

        return window;
    }

    private void OnNewMessageReceived(object? sender, BackgroundMessageService.NewMessageEventArgs e)
    {
        Debug.WriteLine($"[App] Новое сообщение от {e.SenderName}: {e.Message.Text}");

        if (IsAppInForeground && IsChatPageActive)
        {
            Debug.WriteLine("[App] Чат открыт — уведомление не показываем");
            return;
        }

        try
        {
            string messagePreview = e.Message.Type switch
            {
                "file" => $"📎 {e.Message.FileName}",
                "image" => $"🖼️ {e.Message.FileName ?? "Изображение"}",
                _ => e.Message.Text.Length > 80
                    ? e.Message.Text[..80] + "..."
                    : e.Message.Text
            };

            NotificationService.ShowInstantNotification(
                $"💬 {e.SenderName}",
                messagePreview,
                NotificationService.CHAT_CHANNEL_ID);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка при показе уведомления: {ex.Message}");
        }
    }
}