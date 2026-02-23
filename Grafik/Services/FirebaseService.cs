using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Grafik.Services
{
    public class FirebaseMessage
    {
        [JsonPropertyName("sender")]
        public string Sender { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Если это файл/изображение — содержимое в Base64
        /// </summary>
        [JsonPropertyName("fileData")]
        public string? FileData { get; set; }
        
        /// <summary>
        /// Имя файла (если это сообщение с файлом/изображением)
        /// </summary>
        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }
        
        /// <summary>
        /// Размер файла в байтах
        /// </summary>
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }
        
        /// <summary>
        /// Тип: "text", "file" или "image"
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        /// <summary>
        /// Закреплено ли сообщение (для файлов)
        /// </summary>
        [JsonPropertyName("isPinned")]
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Словарь устройств, которые уже прочитали это сообщение.
        /// Ключ — deviceId, значение — true.
        /// </summary>
        [JsonPropertyName("readBy")]
        public Dictionary<string, bool>? ReadBy { get; set; }

        /// <summary>
        /// Ключ записи в Firebase (например -Oi1-wus00QacrG69a_4).
        /// Заполняется при чтении из базы, НЕ сериализуется при отправке.
        /// </summary>
        [JsonIgnore]
        public string FirebaseKey { get; set; } = string.Empty;
    }

    public class FirebaseService
    {
        private readonly string _databaseUrl;
        private readonly HttpClient _httpClient;
        
        /// <summary>
        /// Количество дней для хранения сообщений
        /// </summary>
        private const int MessageRetentionDays = 30;
        
        /// <summary>
        /// Максимальный размер файла в байтах (5 MB)
        /// </summary>
        private const long MaxFileSizeBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Максимальный размер изображения в байтах (3 MB)
        /// </summary>
        private const long MaxImageSizeBytes = 3 * 1024 * 1024;

        /// <summary>
        /// Поддерживаемые расширения изображений
        /// </summary>
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
        };

        public FirebaseService(string firebaseUrl)
        {
            _databaseUrl = firebaseUrl.TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            Log($"🔥 FirebaseService инициализирован с URL: {_databaseUrl}");
            Log($"📋 Сообщения хранятся {MessageRetentionDays} дней");
            Log($"📎 Максимальный размер файла: {MaxFileSizeBytes / (1024 * 1024)} MB");
            Log($"🖼️ Максимальный размер изображения: {MaxImageSizeBytes / (1024 * 1024)} MB");
            
            // Асинхронно очищаем старые сообщения при инициализации
            _ = CleanupOldMessagesAsync();
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMsg = $"[{timestamp}] {message}";

            System.Diagnostics.Debug.WriteLine(logMsg);
            System.Diagnostics.Trace.WriteLine(logMsg);
            Console.WriteLine(logMsg);
        }

        /// <summary>
        /// Получить уникальный идентификатор устройства.
        /// Генерируется один раз и сохраняется в Preferences.
        /// </summary>
        public static string GetDeviceId()
        {
            const string key = "UniqueDeviceId";
            var deviceId = Preferences.Get(key, string.Empty);

            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N")[..12];
                Preferences.Set(key, deviceId);
                Debug.WriteLine($"[FirebaseService] Новый DeviceId создан: {deviceId}");
            }

            return deviceId;
        }

        /// <summary>
        /// Проверяет, является ли файл изображением по расширению
        /// </summary>
        public static bool IsImageFile(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
        }

        /// <summary>
        /// Отправить текстовое сообщение в Firebase
        /// </summary>
        public async Task<bool> SendMessageAsync(string sender, string messageText)
        {
            try
            {
                Log($"📝 Подготовка сообщения от {sender}");

                var deviceId = GetDeviceId();

                var message = new FirebaseMessage
                {
                    Sender = sender,
                    Text = messageText,
                    Timestamp = DateTime.UtcNow,
                    Type = "text",
                    // Отправитель сразу считается «прочитавшим» своё сообщение
                    ReadBy = new Dictionary<string, bool> { { deviceId, true } }
                };

                var json = JsonSerializer.Serialize(message);
                Log($"📝 JSON: {json}");
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var url = $"{_databaseUrl}/messages.json";
                Log($"📍 POST URL: {url}");

                Log($"⏳ Отправка POST запроса...");
                var response = await _httpClient.PostAsync(url, content);

                Log($"📊 Status Code: {(int)response.StatusCode} ({response.StatusCode})");
                Log($"📊 IsSuccessStatusCode: {response.IsSuccessStatusCode}");
                
                var responseBody = await response.Content.ReadAsStringAsync();
                Log($"📨 Response Body Length: {responseBody.Length}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Сообщение успешно отправлено: {sender} - {messageText}");
                    return true;
                }

                Log($"❌ Ошибка отправки: {response.StatusCode}");
                Log($"❌ Response: {responseBody}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                Log($"⏱️ Timeout: {ex.Message}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                Log($"🌐 Ошибка сети: {ex.Message}");
                Log($"🌐 Stack: {ex.StackTrace}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение: {ex.GetType().Name}");
                Log($"❌ Сообщение: {ex.Message}");
                Log($"❌ Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Отправить файл в Firebase (как Base64)
        /// </summary>
        public async Task<bool> SendFileMessageAsync(string sender, string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Log($"❌ Файл не найден: {filePath}");
                    return false;
                }

                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                
                Log($"📎 Подготовка файла для отправки: {fileName} ({fileInfo.Length} байт)");

                // Проверяем размер файла
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    Log($"❌ Файл слишком большой: {fileInfo.Length / (1024 * 1024)} MB (макс. {MaxFileSizeBytes / (1024 * 1024)} MB)");
                    return false;
                }

                // Читаем файл и кодируем в Base64
                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var base64Data = Convert.ToBase64String(fileBytes);
                
                Log($"📎 Файл закодирован в Base64 ({base64Data.Length} символов)");

                var deviceId = GetDeviceId();

                var message = new FirebaseMessage
                {
                    Sender = sender,
                    Text = $"📎 Поделился файлом: {fileName}",
                    Timestamp = DateTime.UtcNow,
                    Type = "file",
                    FileName = fileName,
                    FileData = base64Data,
                    FileSize = fileInfo.Length,
                    ReadBy = new Dictionary<string, bool> { { deviceId, true } }
                };

                var json = JsonSerializer.Serialize(message);
                Log($"📎 Размер сообщения в Firebase: {json.Length} символов");
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var url = $"{_databaseUrl}/messages.json";
                Log($"📍 POST URL: {url}");

                Log($"⏳ Отправка файла...");
                var response = await _httpClient.PostAsync(url, content);

                Log($"📊 Status Code: {(int)response.StatusCode} ({response.StatusCode})");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Файл успешно отправлен: {fileName}");
                    return true;
                }

                Log($"❌ Ошибка отправки файла: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение при отправке файла: {ex.Message}");
                Log($"❌ Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Отправить изображение в Firebase (как Base64)
        /// </summary>
        public async Task<bool> SendImageMessageAsync(string sender, string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    Log($"❌ Изображение не найдено: {imagePath}");
                    return false;
                }

                var fileName = Path.GetFileName(imagePath);
                var fileInfo = new FileInfo(imagePath);

                Log($"🖼️ Подготовка изображения: {fileName} ({fileInfo.Length} байт)");

                if (fileInfo.Length > MaxImageSizeBytes)
                {
                    Log($"❌ Изображение слишком большое: {fileInfo.Length / (1024 * 1024)} MB (макс. {MaxImageSizeBytes / (1024 * 1024)} MB)");
                    return false;
                }

                var fileBytes = await File.ReadAllBytesAsync(imagePath);
                var base64Data = Convert.ToBase64String(fileBytes);

                Log($"🖼️ Изображение закодировано в Base64 ({base64Data.Length} символов)");

                var deviceId = GetDeviceId();

                var message = new FirebaseMessage
                {
                    Sender = sender,
                    Text = $"🖼️ Отправил изображение: {fileName}",
                    Timestamp = DateTime.UtcNow,
                    Type = "image",
                    FileName = fileName,
                    FileData = base64Data,
                    FileSize = fileInfo.Length,
                    ReadBy = new Dictionary<string, bool> { { deviceId, true } }
                };

                var json = JsonSerializer.Serialize(message);
                Log($"🖼️ Размер сообщения: {json.Length} символов");

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var url = $"{_databaseUrl}/messages.json";
                var response = await _httpClient.PostAsync(url, content);

                Log($"📊 Status Code: {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Изображение успешно отправлено: {fileName}");
                    return true;
                }

                Log($"❌ Ошибка отправки изображения: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение при отправке изображения: {ex.Message}");
                Log($"❌ Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Извлечь файл из сообщения и сохранить локально
        /// Возвращает путь к сохраненному файлу
        /// </summary>
        public async Task<string?> ExtractFileAsync(FirebaseMessage message)
        {
            try
            {
                if ((message.Type != "file" && message.Type != "image") 
                    || string.IsNullOrEmpty(message.FileData) 
                    || string.IsNullOrEmpty(message.FileName))
                {
                    Log($"❌ Сообщение не содержит файл");
                    return null;
                }

                Log($"📥 Декодирование файла: {message.FileName}");

                // Декодируем Base64
                var fileBytes = Convert.FromBase64String(message.FileData);
                Log($"📥 Декодировано {fileBytes.Length} байт");

                // Сохраняем файл локально
                var filePath = Path.Combine(FileSystem.AppDataDirectory, message.FileName);
                await File.WriteAllBytesAsync(filePath, fileBytes);
                
                Log($"✅ Файл сохранен: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка при извлечении файла: {ex.Message}");
                Log($"❌ Stack: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Получить все сообщения из Firebase (с ключами Firebase)
        /// </summary>
        public async Task<List<FirebaseMessage>> GetMessagesAsync()
        {
            try
            {
                var url = $"{_databaseUrl}/messages.json";
                Log($"📍 GET URL: {url}");
                
                var response = await _httpClient.GetAsync(url);
                Log($"📊 GET Status: {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Log($"❌ Ошибка получения: {response.StatusCode}");
                    return new List<FirebaseMessage>();
                }

                var json = await response.Content.ReadAsStringAsync();
                Log($"✅ Получено {json.Length} байт");

                if (json == "null")
                {
                    Log($"ℹ️ Firebase вернул null (нет сообщений)");
                    return new List<FirebaseMessage>();
                }

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
                                // Сохраняем ключ Firebase для последующего удаления/обновления
                                message.FirebaseKey = kvp.Key;
                                messages.Add(message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"⚠️ Ошибка десериализации: {ex.Message}");
                        }
                    }
                }

                Log($"✅ Загружено {messages.Count} сообщений");
                return messages.OrderBy(m => m.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение при GET: {ex.Message}");
                return new List<FirebaseMessage>();
            }
        }

        /// <summary>
        /// Получить сообщения после определённой временной метки (для полинга)
        /// </summary>
        public async Task<List<FirebaseMessage>> GetMessagesAfterAsync(DateTime since)
        {
            var allMessages = await GetMessagesAsync();
            return allMessages.Where(m => m.Timestamp > since).ToList();
        }

        /// <summary>
        /// Получить непрочитанные сообщения для текущего устройства.
        /// Возвращает сообщения, в которых readBy не содержит текущий deviceId.
        /// </summary>
        public async Task<List<FirebaseMessage>> GetUnreadMessagesAsync()
        {
            var deviceId = GetDeviceId();
            var allMessages = await GetMessagesAsync();

            return allMessages
                .Where(m => m.ReadBy == null || !m.ReadBy.ContainsKey(deviceId))
                .ToList();
        }

        /// <summary>
        /// Пометить сообщение как прочитанное на текущем устройстве (PATCH readBy.{deviceId} = true)
        /// </summary>
        public async Task<bool> MarkMessageAsReadAsync(string firebaseKey)
        {
            try
            {
                var deviceId = GetDeviceId();
                var url = $"{_databaseUrl}/messages/{firebaseKey}/readBy/{deviceId}.json";

                var content = new StringContent("true", System.Text.Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Сообщение {firebaseKey} помечено как прочитанное для {deviceId}");
                    return true;
                }

                Log($"❌ Ошибка пометки прочитанного: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка MarkMessageAsReadAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Пометить несколько сообщений как прочитанные (пакетно)
        /// </summary>
        public async Task MarkMessagesAsReadAsync(IEnumerable<FirebaseMessage> messages)
        {
            foreach (var msg in messages)
            {
                if (!string.IsNullOrEmpty(msg.FirebaseKey))
                {
                    await MarkMessageAsReadAsync(msg.FirebaseKey);
                }
            }
        }

        /// <summary>
        /// Удалить сообщение по ключу Firebase (например -Oi1-wus00QacrG69a_4)
        /// </summary>
        public async Task<bool> DeleteMessageByFirebaseKeyAsync(string firebaseKey)
        {
            try
            {
                var url = $"{_databaseUrl}/messages/{firebaseKey}.json";
                Log($"🗑️ DELETE URL: {url}");

                var request = new HttpRequestMessage(HttpMethod.Delete, url);
                var response = await _httpClient.SendAsync(request);

                Log($"📊 DELETE Status: {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Удалено по ключу Firebase: {firebaseKey}");
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
        /// Удалить сообщение по внутреннему ID (находит ключ Firebase, затем удаляет)
        /// </summary>
        public async Task<bool> DeleteMessageAsync(string messageId)
        {
            try
            {
                // Находим ключ Firebase по внутреннему ID
                var allMessages = await GetMessagesAsync();
                var message = allMessages.FirstOrDefault(m => m.Id == messageId);

                if (message == null || string.IsNullOrEmpty(message.FirebaseKey))
                {
                    Log($"❌ Сообщение с ID {messageId} не найдено в Firebase");
                    return false;
                }

                return await DeleteMessageByFirebaseKeyAsync(message.FirebaseKey);
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка удаления: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Удалить все старые сообщения (старше N дней).
        /// Не удаляет закреплённые сообщения.
        /// Вызывается автоматически при инициализации сервиса.
        /// </summary>
        public async Task<int> CleanupOldMessagesAsync()
        {
            try
            {
                Log($"🧹 Начинаем очистку сообщений старше {MessageRetentionDays} дней...");
                
                var cutoffTime = DateTime.UtcNow.AddDays(-MessageRetentionDays);
                var allMessages = await GetMessagesAsync();
                
                var oldMessages = allMessages
                    .Where(m => m.Timestamp < cutoffTime && !m.IsPinned)
                    .ToList();

                if (oldMessages.Count == 0)
                {
                    Log($"✅ Нет старых сообщений для удаления");
                    return 0;
                }

                Log($"🗑️ Найдено {oldMessages.Count} старых сообщений для удаления");

                int deletedCount = 0;
                foreach (var message in oldMessages)
                {
                    if (string.IsNullOrEmpty(message.FirebaseKey))
                    {
                        Log($"⚠️ Пропущено сообщение без FirebaseKey: {message.Id}");
                        continue;
                    }

                    if (await DeleteMessageByFirebaseKeyAsync(message.FirebaseKey))
                    {
                        deletedCount++;
                        Log($"🗑️ Удалено: {message.Sender} - {message.Text[..Math.Min(30, message.Text.Length)]}...");
                    }
                    else
                    {
                        Log($"❌ Не удалось удалить: {message.FirebaseKey} ({message.Sender})");
                    }
                }

                Log($"✅ Успешно удалено {deletedCount} старых сообщений");
                return deletedCount;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка при очистке: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Очистить все сообщения
        /// </summary>
        public async Task<bool> ClearAllMessagesAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_databaseUrl}/messages.json");
                var response = await _httpClient.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка очистки: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обновить сообщение (например, статус закрепления)
        /// </summary>
        public async Task<bool> UpdateMessageAsync(FirebaseMessage message)
        {
            try
            {
                // Определяем ключ: используем FirebaseKey, если есть
                var key = !string.IsNullOrEmpty(message.FirebaseKey) ? message.FirebaseKey : message.Id;

                Log($"📝 Обновление сообщения: {key}");

                var json = JsonSerializer.Serialize(message);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var url = $"{_databaseUrl}/messages/{key}.json";
                Log($"📍 PUT URL: {url}");

                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
                var response = await _httpClient.SendAsync(request);

                Log($"📊 Status Code: {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Сообщение успешно обновлено: {key}");
                    return true;
                }

                Log($"❌ Ошибка обновления: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение при обновлении: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обновить статус закрепления сообщения (PATCH для частичного обновления)
        /// </summary>
        public async Task<bool> UpdateMessagePinnedStatusAsync(FirebaseMessage message)
        {
            try
            {
                Log($"📝 Обновление статуса закрепления для: {message.Id}");

                // Используем сохранённый FirebaseKey, если есть
                var firebaseKey = message.FirebaseKey;

                // Если FirebaseKey не заполнен — ищем по ID
                if (string.IsNullOrEmpty(firebaseKey))
                {
                    var firebaseData = await GetRawMessagesDataAsync();
                    
                    foreach (var kvp in firebaseData)
                    {
                        var msg = JsonSerializer.Deserialize<FirebaseMessage>(kvp.Value.GetRawText());
                        if (msg?.Id == message.Id)
                        {
                            firebaseKey = kvp.Key;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(firebaseKey))
                {
                    Log($"❌ Ключ сообщения не найден: {message.Id}");
                    return false;
                }

                Log($"📍 Найден ключ Firebase: {firebaseKey}");

                // Обновляем только поле isPinned через PATCH
                var updateData = new { isPinned = message.IsPinned };
                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var url = $"{_databaseUrl}/messages/{firebaseKey}.json";
                Log($"📍 PATCH URL: {url}");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                var response = await _httpClient.SendAsync(request);

                Log($"📊 Status Code: {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    Log($"✅ Статус закрепления обновлен: {message.Id} -> {message.IsPinned}");
                    return true;
                }

                Log($"❌ Ошибка обновления: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"❌ Исключение при обновлении: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Получить сырые данные сообщений (с ключами Firebase)
        /// </summary>
        private async Task<Dictionary<string, JsonElement>> GetRawMessagesDataAsync()
        {
            try
            {
                var url = $"{_databaseUrl}/messages.json";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return new Dictionary<string, JsonElement>();
                }

                var json = await response.Content.ReadAsStringAsync();
                if (json == "null")
                {
                    return new Dictionary<string, JsonElement>();
                }

                var firebaseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                return firebaseData ?? new Dictionary<string, JsonElement>();
            }
            catch
            {
                return new Dictionary<string, JsonElement>();
            }
        }
    }
}