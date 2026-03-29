using GrafikAdmin.Services;
using GrafikShared.Services;

namespace GrafikAdmin;

public partial class SettingsPage : ContentPage
{
    private const string DefaultFirebaseUrl = "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/";

    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        FirebaseUrlEntry.Text = Preferences.Get("FirebaseUrl", DefaultFirebaseUrl);
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        var url = FirebaseUrlEntry.Text?.Trim();

        if (string.IsNullOrEmpty(url))
            url = DefaultFirebaseUrl;

        ConnectionStatus.Text = "⏳ Проверка...";
        ConnectionStatus.TextColor = Colors.Gray;

        try
        {
            var service = new FirebaseServiceBase(url);
            var messages = await service.GetMessagesAsync();

            ConnectionStatus.Text = $"✅ Подключено! Сообщений: {messages.Count}";
            ConnectionStatus.TextColor = Colors.Green;
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"❌ Ошибка: {ex.Message}";
            ConnectionStatus.TextColor = Colors.Red;
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var url = FirebaseUrlEntry.Text?.Trim();

        if (string.IsNullOrEmpty(url))
            url = DefaultFirebaseUrl;

        Preferences.Set("FirebaseUrl", url);

        // Перезапускаем мониторинг с новым URL
        FirebaseConnectionMonitor.Instance.Restart();

        await DisplayAlert("✅ Успех", "Настройки сохранены", "OK");
        await Navigation.PopAsync();
    }
}