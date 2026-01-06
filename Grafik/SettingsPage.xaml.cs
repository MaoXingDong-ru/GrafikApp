using Grafik.Services;
using System.Text.Json;

namespace Grafik;
public partial class SettingsPage : ContentPage
{
    private readonly string settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");

    public SettingsPage()
    {
        InitializeComponent();
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
                ReminderPicker.SelectedIndex = (int)settings.Reminder; // устанавливаем выбранное напоминание
            }
        }
    }

    private void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        var selectedReminder = (ReminderOption)ReminderPicker.SelectedIndex;

        var settings = new AppSettings
        {
            Reminder = selectedReminder
        };

        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(settingsFilePath, json);

        DisplayAlert("Сохранено", "Настройки успешно сохранены", "OK");
    }
}

public class AppSettings
{
    public ReminderOption Reminder { get; set; }
}
