using System.Text;
using System.Text.Json;
using GrafikShared.Models;

namespace GrafikShared.Services;

/// <summary>
/// Базовый сервис для работы с Firebase (общий для обоих приложений)
/// </summary>
public class FirebaseServiceBase
{
    protected readonly string DatabaseUrl;
    protected readonly HttpClient HttpClient;

    public FirebaseServiceBase(string firebaseUrl)
    {
        DatabaseUrl = firebaseUrl.TrimEnd('/');
        HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Получить все сообщения из Firebase
    /// </summary>
    public virtual async Task<List<FirebaseMessage>> GetMessagesAsync()
    {
        try
        {
            var url = $"{DatabaseUrl}/messages.json";
            var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync();

            if (json == "null")
                return [];

            var firebaseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var messages = new List<FirebaseMessage>();

            if (firebaseData != null)
            {
                foreach (var kvp in firebaseData)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<FirebaseMessage>(kvp.Value.GetRawText());
                        if (message != null)
                        {
                            message.FirebaseKey = kvp.Key;
                            messages.Add(message);
                        }
                    }
                    catch { /* Пропускаем битые записи */ }
                }
            }

            return messages.OrderBy(m => m.Timestamp).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Отправить текстовое сообщение
    /// </summary>
    public virtual async Task<bool> SendMessageAsync(string sender, string text, string? deviceId = null)
    {
        try
        {
            var message = new FirebaseMessage
            {
                Sender = sender,
                Text = text,
                Timestamp = DateTime.UtcNow,
                Type = "text",
                ReadBy = deviceId != null ? new Dictionary<string, bool> { { deviceId, true } } : null
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{DatabaseUrl}/messages.json";

            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отправить файл в Firebase
    /// </summary>
    public virtual async Task<bool> SendFileAsync(string sender, string filePath, string? deviceId = null)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var fileName = Path.GetFileName(filePath);
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var base64Data = Convert.ToBase64String(fileBytes);

            var message = new FirebaseMessage
            {
                Sender = sender,
                Text = $"📎 Файл: {fileName}",
                Timestamp = DateTime.UtcNow,
                Type = "file",
                FileName = fileName,
                FileData = base64Data,
                FileSize = fileBytes.Length,
                ReadBy = deviceId != null ? new Dictionary<string, bool> { { deviceId, true } } : null
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{DatabaseUrl}/messages.json";

            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}