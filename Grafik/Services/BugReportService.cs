using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Grafik.Services;

/// <summary>
/// Сервис для работы с баг-репортами через Firebase Realtime Database.
/// Хранит данные в узле /service.
/// </summary>
public class BugReportService
{
    private const string FirebaseNode = "service";

    private readonly string _databaseUrl;
    private readonly HttpClient _httpClient;

    public BugReportService(string firebaseUrl)
    {
        _databaseUrl = firebaseUrl.TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Автоочистка старых завершённых репортов при инициализации
        _ = CleanupOldReportsAsync();
    }

    private static void Log(string message)
    {
        Debug.WriteLine($"[BugReportService] {message}");
    }

    /// <summary>
    /// Отправить новый баг-репорт / предложение
    /// </summary>
    public async Task<bool> SendBugReportAsync(BugReport report)
    {
        try
        {
            var json = JsonSerializer.Serialize(report);
            Log($"📝 JSON: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_databaseUrl}/{FirebaseNode}.json";
            Log($"📍 POST URL: {url}");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            Log($"📊 Status: {(int)response.StatusCode}, Response: {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ Баг-репорт отправлен: {report.Title}");
                return true;
            }

            Log($"❌ Ошибка отправки: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Исключение: {ex.Message}");
            Log($"❌ Stack: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Получить все баг-репорты из Firebase
    /// </summary>
    public async Task<List<BugReport>> GetBugReportsAsync()
    {
        try
        {
            var url = $"{_databaseUrl}/{FirebaseNode}.json";
            Log($"📍 GET URL: {url}");

            var response = await _httpClient.GetAsync(url);
            Log($"📊 GET Status: {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Log($"❌ Ошибка получения: {response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            Log($"✅ Получено {json.Length} байт");

            if (json == "null")
                return [];

            var firebaseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var reports = new List<BugReport>();

            if (firebaseData != null)
            {
                foreach (var kvp in firebaseData)
                {
                    try
                    {
                        var report = JsonSerializer.Deserialize<BugReport>(kvp.Value.GetRawText());
                        if (report != null)
                        {
                            report.FirebaseKey = kvp.Key;
                            reports.Add(report);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Ошибка десериализации: {ex.Message}");
                    }
                }
            }

            Log($"✅ Загружено {reports.Count} баг-репортов");
            return reports.OrderByDescending(r => r.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            Log($"❌ Исключение: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Обновить статус и комментарий разработчика одним запросом (PATCH)
    /// </summary>
    public async Task<bool> UpdateStatusAndCommentAsync(string firebaseKey, string status, string comment)
    {
        try
        {
            var updateData = new { status, devComment = comment };
            var json = JsonSerializer.Serialize(updateData);
            Log($"📝 PATCH: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            Log($"📍 PATCH URL: {url}");

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            Log($"📊 PATCH Status: {(int)response.StatusCode}, Response: {responseBody}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка обновления: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Обновить комментарий разработчика (PATCH)
    /// </summary>
    public async Task<bool> UpdateDevCommentAsync(string firebaseKey, string comment)
    {
        try
        {
            var updateData = new { devComment = comment };
            var json = JsonSerializer.Serialize(updateData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await _httpClient.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка обновления комментария: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Обновить статус баг-репорта (PATCH)
    /// </summary>
    public async Task<bool> UpdateStatusAsync(string firebaseKey, string status)
    {
        try
        {
            var updateData = new { status };
            var json = JsonSerializer.Serialize(updateData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await _httpClient.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка обновления статуса: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Удалить репорт по ключу Firebase
    /// </summary>
    public async Task<bool> DeleteReportAsync(string firebaseKey)
    {
        try
        {
            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            Log($"🗑️ DELETE URL: {url}");

            var response = await _httpClient.DeleteAsync(url);
            Log($"📊 DELETE Status: {(int)response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ Удалён репорт: {firebaseKey}");
                return true;
            }

            Log($"❌ Ошибка удаления: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка удаления: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Автоочистка старых репортов со статусом «resolved» или «rejected»,
    /// которые старше BugReport.RetentionDays дней.
    /// Репорты со статусом «open» и «inprogress» НЕ удаляются.
    /// </summary>
    public async Task<int> CleanupOldReportsAsync()
    {
        try
        {
            Log($"🧹 Начинаем очистку репортов старше {BugReport.RetentionDays} дней (только resolved/rejected)...");

            var allReports = await GetBugReportsAsync();
            var cutoffTime = DateTime.UtcNow.AddDays(-BugReport.RetentionDays);

            var oldReports = allReports
                .Where(r => BugReport.CleanupStatuses.Contains(r.Status) && r.Timestamp < cutoffTime)
                .ToList();

            if (oldReports.Count == 0)
            {
                Log("✅ Нет старых завершённых репортов для удаления");
                return 0;
            }

            Log($"🗑️ Найдено {oldReports.Count} репортов для удаления");

            int deletedCount = 0;
            foreach (var report in oldReports)
            {
                if (string.IsNullOrEmpty(report.FirebaseKey))
                    continue;

                if (await DeleteReportAsync(report.FirebaseKey))
                {
                    deletedCount++;
                    Log($"🗑️ Удалён: [{report.StatusDisplay}] {report.Title}");
                }
            }

            Log($"✅ Удалено {deletedCount} старых репортов");
            return deletedCount;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка очистки: {ex.Message}");
            return 0;
        }
    }
}
