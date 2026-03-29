using GrafikAdmin.Services;
using System.Diagnostics;

namespace GrafikAdmin;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        
        // Принудительно устанавливаем тёмную тему
        UserAppTheme = AppTheme.Dark;
        
        Debug.WriteLine("[App] Конструктор App вызван");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Debug.WriteLine("[App] CreateWindow вызван");
        
        var mainPage = new MainPage();
        var window = new Window(new NavigationPage(mainPage));
        
        // Запускаем мониторинг ПОСЛЕ создания окна
        window.Created += (s, e) =>
        {
            Debug.WriteLine("[App] Window.Created - запуск мониторинга");
            
            // Проверяем URL
            var url = Preferences.Get("FirebaseUrl", string.Empty);
            Debug.WriteLine($"[App] Firebase URL: '{url}'");
            
            FirebaseConnectionMonitor.Instance.Start();
        };

        return window;
    }
}