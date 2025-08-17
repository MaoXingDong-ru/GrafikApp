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
                FirstLineCountEntry.Text = settings.EmployeeCount.ToString();
                SecondLineCountEntry.Text = settings.SecondLineEmployeeCount.ToString();
                ReminderPicker.SelectedIndex = (int)settings.Reminder; // ������������� ��������� �����������
            }
        }
    }

    private void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(FirstLineCountEntry.Text, out int firstLineCount) || firstLineCount <= 0)
        {
            DisplayAlert("������", "������� ���������� ���������� ����������� ������ �����", "OK");
            return;
        }

        int secondLineCount = 0;
        if (!int.TryParse(SecondLineCountEntry.Text, out secondLineCount) || secondLineCount <= 0)
        {
            DisplayAlert("������", "������� ���������� ���������� ����������� ������ �����", "OK");
            return;
        }

        var selectedReminder = (ReminderOption)ReminderPicker.SelectedIndex;

        var settings = new AppSettings
        {
            EmployeeCount = firstLineCount,
            SecondLineEmployeeCount = secondLineCount,
            Reminder = selectedReminder
        };

        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(settingsFilePath, json);

        DisplayAlert("���������", "��������� ������� ���������", "OK");
    }
}

public class AppSettings
{
    public int EmployeeCount { get; set; }
    public int SecondLineEmployeeCount { get; set; }
    public bool HasSecondLineEmployees { get; set; }
    public ReminderOption Reminder { get; set; }
}
