using Grafik.Services;
using System.Text.Json;

namespace Grafik;

public partial class SettingsPage : ContentPage
{
    private readonly string settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");
    
    /// <summary>
    /// Firebase URL по умолчанию
    /// </summary>
    private const string DefaultFirebaseUrl = "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/";

    /// <summary>
    /// Маппинг индексов Picker на ReminderOption
    /// </summary>
    private static readonly ReminderOption[] ReminderOptions =
    [
        ReminderOption.FifteenMinutesBefore,  // 0 - "За 15 минут"
        ReminderOption.OneHourBefore,          // 1 - "За 1 час"
        ReminderOption.OneDayBefore            // 2 - "За 1 день"
    ];
 
    public SettingsPage()
    {
        InitializeComponent();

        // Инициализация Picker
        ReminderPicker.ItemsSource = new List<string>
        {
            "За 15 минут",
            "За 1 час",
            "За 12 часов"
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        if (File.Exists(settingsFilePath))
        {
            string jsonData = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(jsonData);

            if (settings != null)
            {
                // Находим индекс по значению ReminderOption
                ReminderPicker.SelectedIndex = GetPickerIndexFromReminder(settings.Reminder);
                FirebaseUrlEntry.Text = settings.FirebaseUrl ?? DefaultFirebaseUrl;

                // Обновляем UI статуса
                UpdateFirebaseStatus(settings);
            }
        }
        else
        {
            // Если файла нет, используем дефолт (15 минут)
            ReminderPicker.SelectedIndex = 0;
            FirebaseUrlEntry.Text = DefaultFirebaseUrl;
        }
    }

    /// <summary>
    /// Получить индекс Picker из ReminderOption
    /// </summary>
    private static int GetPickerIndexFromReminder(ReminderOption reminder)
    {
        for (int i = 0; i < ReminderOptions.Length; i++)
        {
            if (ReminderOptions[i] == reminder)
                return i;
        }
        return 0; // По умолчанию "За 15 минут"
    }

    /// <summary>
    /// Получить ReminderOption из индекса Picker
    /// </summary>
    private static ReminderOption GetReminderFromPickerIndex(int index)
    {
        if (index >= 0 && index < ReminderOptions.Length)
            return ReminderOptions[index];
        return ReminderOption.FifteenMinutesBefore;
    }

    private void UpdateFirebaseStatus(AppSettings settings)
    {
        // Используем дефолт, если URL пуст
        var firebaseUrl = string.IsNullOrEmpty(settings.FirebaseUrl) ? DefaultFirebaseUrl : settings.FirebaseUrl;
        
        if (!string.IsNullOrEmpty(firebaseUrl))
        {
            FirebaseStatusLabel.Text = "✓ Firebase настроен";
            FirebaseStatusLabel.TextColor = Colors.Green;
        }
        else
        {
            FirebaseStatusLabel.Text = "○ Firebase не настроен";
            FirebaseStatusLabel.TextColor = Colors.Orange;
        }
    }

    private async void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        var settings = LoadCurrentSettings();

        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(settingsFilePath, json);

        // Сохраняем Firebase URL в Preferences (или дефолт, если пусто)
        var urlToSave = string.IsNullOrEmpty(settings.FirebaseUrl) ? DefaultFirebaseUrl : settings.FirebaseUrl;
        Preferences.Set("FirebaseUrl", urlToSave);

        // Сохраняем ReminderOption в Preferences для использования NotificationService
        // Сохраняем числовое значение enum для надёжного парсинга
        Preferences.Set("ReminderOption", ((int)settings.Reminder).ToString());

        System.Diagnostics.Debug.WriteLine($"[SettingsPage] Сохранено напоминание: {settings.Reminder} ({(int)settings.Reminder})");

        // Перепланируем уведомления с новыми настройками
        RescheduleNotificationsAfterSave(settings.Reminder);

        await DisplayAlert("Успех", "Параметры сохранены", "OK");
    }

    /// <summary>
    /// Перепланировать уведомления после сохранения настроек
    /// </summary>
    private static void RescheduleNotificationsAfterSave(ReminderOption reminder)
    {
        try
        {
            var employeeName = Preferences.Get("SelectedEmployee", string.Empty);
            
            if (string.IsNullOrEmpty(employeeName))
            {
                System.Diagnostics.Debug.WriteLine("[SettingsPage] Сотрудник не выбран — пропуск перепланирования");
                return;
            }

            var schedulePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");
            if (!File.Exists(schedulePath))
            {
                System.Diagnostics.Debug.WriteLine("[SettingsPage] Файл расписания не найден — пропуск перепланирования");
                return;
            }

            var jsonData = File.ReadAllText(schedulePath);
            var allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(jsonData) ?? [];

            System.Diagnostics.Debug.WriteLine($"[SettingsPage] Перепланирование уведомлений для {employeeName} с напоминанием: {reminder}");

            NotificationService.RescheduleAllForEmployee(employeeName, allSchedule, reminder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] ❌ Ошибка перепланирования: {ex.Message}");
        }
    }

    private AppSettings LoadCurrentSettings()
    {
        AppSettings settings = new();

        if (File.Exists(settingsFilePath))
        {
            string jsonData = File.ReadAllText(settingsFilePath);
            settings = JsonSerializer.Deserialize<AppSettings>(jsonData) ?? settings;
        }

        // Используем корректный маппинг индекса → ReminderOption
        settings.Reminder = GetReminderFromPickerIndex(ReminderPicker.SelectedIndex);
        settings.FirebaseUrl = FirebaseUrlEntry.Text;

        return settings;
    }

    private async void OnTestFirebaseClicked(object sender, EventArgs e)
    {
        var firebaseUrl = FirebaseUrlEntry.Text?.Trim();
        
        // Если пусто, используем дефолт
        if (string.IsNullOrEmpty(firebaseUrl))
        {
            firebaseUrl = DefaultFirebaseUrl;
        }

        try
        {
            await DisplayAlert("Проверка", "Попытка подключения к Firebase...", "OK");

            var firebaseService = new FirebaseService(firebaseUrl);
            var messages = await firebaseService.GetMessagesAsync();

            await DisplayAlert("Успех", $"Подключение успешно! В базе {messages.Count} сообщений", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось подключиться: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Открыть страницу баг-репортов и предложений
    /// </summary>
    private async void OnBugReportClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new BugReportPage());
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var url = FirebaseUrlEntry.Text?.Trim();

        if (string.IsNullOrEmpty(url))
        {
            await DisplayAlert("Ошибка", "Введите Firebase URL", "OK");
            return;
        }

        Preferences.Set("FirebaseUrl", url);

        // Перезапускаем мониторинг с новым URL
        Services.FirebaseConnectionMonitor.Instance.Restart();

        await DisplayAlert("✅ Успех", "Настройки сохранены", "OK");
        await Navigation.PopAsync();
    }
}

public class AppSettings
{
    public ReminderOption Reminder { get; set; }
    public string? FirebaseUrl { get; set; }
}