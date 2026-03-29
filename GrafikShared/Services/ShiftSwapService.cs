using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GrafikShared.Services;

/// <summary>
/// Сервис для работы с запросами на обмен сменами через Firebase
/// </summary>
public class ShiftSwapService
{
    private const string FirebaseNode = "swapRequests";

    private readonly string _databaseUrl;
    private readonly HttpClient _httpClient;

    public ShiftSwapService(string firebaseUrl)
    {
        _databaseUrl = firebaseUrl.TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static void Log(string message)
    {
        Debug.WriteLine($"[ShiftSwapService] {message}");
    }

    /// <summary>
    /// Создать новый запрос на обмен сменами
    /// </summary>
    public async Task<bool> CreateSwapRequestAsync(ShiftSwapRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            Log($"📝 Создание запроса: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_databaseUrl}/{FirebaseNode}.json";

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            Log($"📊 Status: {(int)response.StatusCode}, Response: {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ Запрос на обмен создан: {request.RequesterName} ↔️ {request.TargetName}");
                return true;
            }

            Log($"❌ Ошибка создания запроса: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Исключение: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Получить все запросы на обмен
    /// </summary>
    public async Task<List<ShiftSwapRequest>> GetAllRequestsAsync()
    {
        try
        {
            var url = $"{_databaseUrl}/{FirebaseNode}.json";
            Log($"📍 GET URL: {url}");

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Log($"❌ Ошибка получения: {response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();

            if (json == "null")
                return [];

            var firebaseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var requests = new List<ShiftSwapRequest>();

            if (firebaseData != null)
            {
                foreach (var kvp in firebaseData)
                {
                    try
                    {
                        var request = JsonSerializer.Deserialize<ShiftSwapRequest>(kvp.Value.GetRawText());
                        if (request != null)
                        {
                            request.FirebaseKey = kvp.Key;
                            requests.Add(request);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Ошибка десериализации: {ex.Message}");
                    }
                }
            }

            Log($"✅ Загружено {requests.Count} запросов на обмен");
            return requests.OrderByDescending(r => r.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            Log($"❌ Исключение: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Получить запросы для конкретного сотрудника (как инициатор или цель)
    /// </summary>
    public async Task<List<ShiftSwapRequest>> GetRequestsForEmployeeAsync(string employeeName)
    {
        var allRequests = await GetAllRequestsAsync();
        return allRequests
            .Where(r => r.RequesterName.Equals(employeeName, StringComparison.OrdinalIgnoreCase) ||
                        r.TargetName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Получить ожидающие запросы (для админа)
    /// </summary>
    public async Task<List<ShiftSwapRequest>> GetPendingRequestsAsync()
    {
        var allRequests = await GetAllRequestsAsync();
        return allRequests.Where(r => r.Status == "pending").ToList();
    }

    /// <summary>
    /// Одобрить запрос на обмен
    /// </summary>
    public async Task<bool> ApproveRequestAsync(string firebaseKey, string adminComment = "")
    {
        return await UpdateRequestStatusAsync(firebaseKey, "approved", adminComment);
    }

    /// <summary>
    /// Отклонить запрос на обмен
    /// </summary>
    public async Task<bool> DenyRequestAsync(string firebaseKey, string adminComment = "")
    {
        return await UpdateRequestStatusAsync(firebaseKey, "denied", adminComment);
    }

    /// <summary>
    /// Обновить статус запроса
    /// </summary>
    private async Task<bool> UpdateRequestStatusAsync(string firebaseKey, string status, string adminComment)
    {
        try
        {
            var updateData = new
            {
                status,
                adminComment,
                processedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(updateData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            Log($"📍 PATCH URL: {url}");

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await _httpClient.SendAsync(request);

            Log($"📊 PATCH Status: {(int)response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ Статус обновлён на: {status}");
                return true;
            }

            Log($"❌ Ошибка обновления: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Удалить запрос (например, отменить до одобрения)
    /// </summary>
    public async Task<bool> DeleteRequestAsync(string firebaseKey)
    {
        try
        {
            var url = $"{_databaseUrl}/{FirebaseNode}/{firebaseKey}.json";
            Log($"🗑️ DELETE URL: {url}");

            var response = await _httpClient.DeleteAsync(url);

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ Запрос удалён: {firebaseKey}");
                return true;
            }

            Log($"❌ Ошибка удаления: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Проверить, есть ли у сотрудника ожидающий запрос на конкретную дату
    /// </summary>
    public async Task<bool> HasPendingRequestForDateAsync(string employeeName, DateTime date)
    {
        var requests = await GetRequestsForEmployeeAsync(employeeName);
        return requests.Any(r => r.Status == "pending" &&
            (r.RequesterDate.Date == date.Date || r.TargetDate.Date == date.Date));
    }
}