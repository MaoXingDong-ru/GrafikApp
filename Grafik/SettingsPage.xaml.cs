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
                ReminderPicker.SelectedIndex = (int)settings.Reminder;
                FirebaseUrlEntry.Text = settings.FirebaseUrl ?? DefaultFirebaseUrl;

                // Обновляем UI статуса
                UpdateFirebaseStatus(settings);
            }
        }
        else
        {
            // Если файла нет, используем дефолт
            FirebaseUrlEntry.Text = DefaultFirebaseUrl;
        }
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

    private void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        var settings = LoadCurrentSettings();

        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(settingsFilePath, json);

        // Сохраняем Firebase URL в Preferences (или дефолт, если пусто)
        var urlToSave = string.IsNullOrEmpty(settings.FirebaseUrl) ? DefaultFirebaseUrl : settings.FirebaseUrl;
        Preferences.Set("FirebaseUrl", urlToSave);

        DisplayAlert("Успех", "Параметры сохранены", "OK");
    }

    private AppSettings LoadCurrentSettings()
    {
        AppSettings settings = new();

        if (File.Exists(settingsFilePath))
        {
            string jsonData = File.ReadAllText(settingsFilePath);
            settings = JsonSerializer.Deserialize<AppSettings>(jsonData) ?? settings;
        }

        settings.Reminder = (ReminderOption)ReminderPicker.SelectedIndex;
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
}

public class AppSettings
{
    public ReminderOption Reminder { get; set; }
    public string? FirebaseUrl { get; set; }
}