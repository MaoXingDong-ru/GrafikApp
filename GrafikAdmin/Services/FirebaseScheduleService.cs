using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrafikAdmin.Models;

namespace GrafikAdmin.Services;

/// <summary>
/// Сообщение для Firebase (аналог FirebaseMessage из Grafik)
/// </summary>
public class FirebaseChatMessage
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("fileData")]
    public string? FileData { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; } = false;

    [JsonPropertyName("readBy")]
    public Dictionary<string, bool>? ReadBy { get; set; }
}

/// <summary>
/// Сервис для выгрузки расписания в чат Firebase
/// </summary>
public class FirebaseScheduleService : IDisposable
{
    private readonly string _databaseUrl;
    private readonly HttpClient _httpClient;
    private Timer? _keepAliveTimer;
    private bool _isWarmedUp = false;

    public FirebaseScheduleService(string firebaseUrl)
    {
        _databaseUrl = firebaseUrl.TrimEnd('/');
        
        // Настраиваем HttpClient для keep-alive
        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = 10
        };
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                ConnectionClose = false // Keep-Alive
            }
        };

        // Запускаем фоновый polling каждые 25 секунд
        _keepAliveTimer = new Timer(KeepAliveCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(25));
    }

    private static void Log(string message) =>
        Debug.WriteLine($"[FirebaseScheduleService] {message}");

    /// <summary>
    /// Callback для поддержания соединения
    /// </summary>
    private async void KeepAliveCallback(object? state)
    {
        try
        {
            var url = $"{_databaseUrl}/messages.json?shallow=true&limitToLast=1";
            var response = await _httpClient.GetAsync(url);
            _isWarmedUp = response.IsSuccessStatusCode;
            Log($"🔄 Keep-alive: {(_isWarmedUp ? "OK ✓" : "FAIL ✗")}");
        }
        catch (Exception ex)
        {
            _isWarmedUp = false;
            Log($"🔄 Keep-alive error: {ex.Message}");
        }
    }

    /// <summary>
    /// Прогреть соединение перед важной операцией
    /// </summary>
    public async Task WarmUpConnectionAsync()
    {
        try
        {
            Log("🔥 Прогрев соединения...");
            
            // Делаем несколько быстрых запросов для установки соединения
            for (int i = 0; i < 2; i++)
            {
                var url = $"{_databaseUrl}/messages.json?shallow=true&limitToLast=1";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    _isWarmedUp = true;
                    Log($"🔥 Прогрев {i + 1}/2: OK ✓");
                }
                else
                {
                    Log($"🔥 Прогрев {i + 1}/2: {response.StatusCode}");
                }
                
                await Task.Delay(100); // Небольшая пауза между запросами
            }
        }
        catch (Exception ex)
        {
            Log($"🔥 Ошибка прогрева: {ex.Message}");
        }
    }

    /// <summary>
    /// Выгрузить расписание как закреплённое сообщение в чат
    /// </summary>
    public async Task<bool> UploadScheduleToChatAsync(MonthlySchedule schedule)
    {
        try
        {
            // Прогреваем соединение перед отправкой
            await WarmUpConnectionAsync();
            
            var fileName = $"{schedule.Month:D2}_{schedule.Year}.json";
            Log($"📤 Подготовка: {fileName}");

            // Конвертируем расписание в JSON
            var scheduleData = new
            {
                year = schedule.Year,
                month = schedule.Month,
                createdAt = schedule.CreatedAt,
                lastModifiedAt = DateTime.UtcNow,
                employees = schedule.Employees,
                secondLineEmployees = schedule.SecondLineEmployees,
                entries = schedule.Entries.Select(e => new
                {
                    employeeName = e.EmployeeName,
                    date = e.Date,
                    shiftType = (int)e.ShiftType,
                    isSecondLine = e.IsSecondLine
                }).ToList()
            };

            var scheduleJson = JsonSerializer.Serialize(scheduleData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Кодируем в Base64
            var jsonBytes = Encoding.UTF8.GetBytes(scheduleJson);
            var base64Data = Convert.ToBase64String(jsonBytes);

            Log($"📎 JSON закодирован: {base64Data.Length} символов");

            // Создаём сообщение для чата
            var message = new FirebaseChatMessage
            {
                Sender = "Админ",
                Text = $"📅 Добавлено расписание: {schedule.DisplayName}",
                Timestamp = DateTime.UtcNow,
                Type = "file",
                FileName = fileName,
                FileData = base64Data,
                FileSize = jsonBytes.Length,
                IsPinned = true,
                ReadBy = []
            };

            var messageJson = JsonSerializer.Serialize(message);
            var content = new StringContent(messageJson, Encoding.UTF8, "application/json");

            var url = $"{_databaseUrl}/messages.json";
            Log($"📍 POST URL: {url}");

            // Retry логика
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Log($"⏳ Попытка {attempt}/{maxRetries}...");
                    
                    var response = await _httpClient.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        Log($"✅ Расписание выгружено в чат: {fileName}");
                        return true;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    Log($"❌ Попытка {attempt}: {response.StatusCode}");
                    Log($"❌ Response: {responseBody}");
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(500 * attempt); // Увеличиваем паузу
                        await WarmUpConnectionAsync(); // Прогреваем заново
                    }
                }
                catch (TaskCanceledException) when (attempt < maxRetries)
                {
                    Log($"⏱️ Timeout на попытке {attempt}, повторяем...");
                    await Task.Delay(500 * attempt);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Исключение: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Проверить подключение к Firebase
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            // Прогреваем перед проверкой
            await WarmUpConnectionAsync();
            
            var response = await _httpClient.GetAsync($"{_databaseUrl}/.json?shallow=true");
            var result = response.IsSuccessStatusCode;
            
            Log($"🔌 Проверка соединения: {(result ? "OK ✓" : "FAIL ✗")}");
            return result;
        }
        catch (Exception ex)
        {
            Log($"🔌 Ошибка проверки: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Статус соединения (прогрето ли)
    /// </summary>
    public bool IsWarmedUp => _isWarmedUp;

    public void Dispose()
    {
        _keepAliveTimer?.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}