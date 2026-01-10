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
        /// Если это файл - содержимое файла в Base64
        /// </summary>
        [JsonPropertyName("fileData")]
        public string? FileData { get; set; }
        
        /// <summary>
        /// Имя файла (если это сообщение с файлом)
        /// </summary>
        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }
        
        /// <summary>
        /// Размер файла в байтах
        /// </summary>
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }
        
        /// <summary>
        /// Тип: "text" или "file"
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";
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

        public FirebaseService(string firebaseUrl)
        {
            _databaseUrl = firebaseUrl.TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            Log($"🔥 FirebaseService инициализирован с URL: {_databaseUrl}");
            Log($"📋 Сообщения хранятся {MessageRetentionDays} дней");
            Log($"📎 Максимальный размер файла: {MaxFileSizeBytes / (1024 * 1024)} MB");
            
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
        /// Отправить текстовое сообщение в Firebase
        /// </summary>
        public async Task<bool> SendMessageAsync(string sender, string messageText)
        {
            try
            {
                Log($"📝 Подготовка сообщения от {sender}");

                var message = new FirebaseMessage
                {
                    Sender = sender,
                    Text = messageText,
                    Timestamp = DateTime.UtcNow,
                    Type = "text"
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

                var message = new FirebaseMessage
                {
                    Sender = sender,
                    Text = $"📎 Поделился файлом: {fileName}",
                    Timestamp = DateTime.UtcNow,
                    Type = "file",
                    FileName = fileName,
                    FileData = base64Data,
                    FileSize = fileInfo.Length
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
        /// Извлечь файл из сообщения и сохранить локально
        /// Возвращает путь к сохраненному файлу
        /// </summary>
        public async Task<string?> ExtractFileAsync(FirebaseMessage message)
        {
            try
            {
                if (message.Type != "file" || string.IsNullOrEmpty(message.FileData) || string.IsNullOrEmpty(message.FileName))
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
        /// Получить все сообщения из Firebase
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
        /// Удалить сообщение по ID
        /// </summary>
        public async Task<bool> DeleteMessageAsync(string messageId)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_databaseUrl}/messages/{messageId}.json");
                var response = await _httpClient.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка удаления: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Удалить все старые сообщения (старше N дней)
        /// Вызывается автоматически при инициализации сервиса
        /// </summary>
        public async Task<int> CleanupOldMessagesAsync()
        {
            try
            {
                Log($"🧹 Начинаем очистку сообщений старше {MessageRetentionDays} дней...");
                
                var cutoffTime = DateTime.UtcNow.AddDays(-MessageRetentionDays);
                var allMessages = await GetMessagesAsync();
                
                var oldMessages = allMessages
                    .Where(m => m.Timestamp < cutoffTime)
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
                    if (await DeleteMessageAsync(message.Id))
                    {
                        deletedCount++;
                        Log($"🗑️ Удалено: {message.Sender} - {message.Text.Substring(0, Math.Min(30, message.Text.Length))}...");
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
    }
}