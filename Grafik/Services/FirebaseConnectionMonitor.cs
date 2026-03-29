using System.Diagnostics;
using System.Text.Json;
using GrafikShared.Services;

namespace Grafik.Services;

/// <summary>
/// Синглтон для мониторинга соединения с Firebase и синхронизации обменов
/// </summary>
public sealed class FirebaseConnectionMonitor : IDisposable
{
    private static FirebaseConnectionMonitor? _instance;
    private static readonly object _lock = new();

    private Timer? _pollingTimer;
    private string _databaseUrl = string.Empty;
    private bool _isConnected;
    private bool _isStarted;

    // Не используем статический HttpClient — пересоздаём при каждой проверке,
    // чтобы избежать кеширования разорванного соединения
    private static HttpClient CreatePingClient() => new(new HttpClientHandler
    {
        // Отключаем keep-alive для ping — каждый запрос создаёт новое соединение
        MaxConnectionsPerServer = 1
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    /// <summary>
    /// Событие изменения статуса соединения
    /// </summary>
    public event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Событие при применении нового обмена (для обновления UI)
    /// </summary>
    public event EventHandler<SwapAppliedEventArgs>? SwapApplied;

    /// <summary>
    /// Текущий статус соединения
    /// </summary>
    public bool IsConnected => _isConnected;

    public static FirebaseConnectionMonitor Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new FirebaseConnectionMonitor();
                }
            }
            return _instance;
        }
    }

    private FirebaseConnectionMonitor()
    {
    }

    /// <summary>
    /// Запустить мониторинг
    /// </summary>
    public void Start()
    {
        if (_isStarted)
        {
            Log("⚠️ Мониторинг уже запущен");
            return;
        }

        _databaseUrl = Preferences.Get("FirebaseUrl", string.Empty).TrimEnd('/');
        Log($"🔧 URL: '{_databaseUrl}'");

        if (string.IsNullOrEmpty(_databaseUrl))
        {
            Log("⚠️ URL пустой!");
            UpdateStatus(false);
            return;
        }

        _isStarted = true;
        _ = PollingTickAsync();

        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(
            async _ => await PollingTickAsync(),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10)
        );

        Log("🚀 Мониторинг запущен");
    }

    /// <summary>
    /// Остановить мониторинг
    /// </summary>
    public void Stop()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isStarted = false;
        Log("⏹️ Мониторинг остановлен");
    }

    /// <summary>
    /// Перезапустить мониторинг (при смене URL)
    /// </summary>
    public void Restart()
    {
        Stop();
        Start();
    }

    /// <summary>
    /// Приостановить polling (приложение свёрнуто)
    /// </summary>
    public void Pause()
    {
        _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        Log("⏸️ Мониторинг приостановлен");
    }

    /// <summary>
    /// Возобновить polling (приложение активно)
    /// </summary>
    public void Resume()
    {
        if (_isStarted && _pollingTimer != null)
        {
            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            Log("▶️ Мониторинг возобновлён");
        }
    }

    /// <summary>
    /// Один тик polling — проверяем соединение и обмены
    /// </summary>
    private async Task PollingTickAsync()
    {
        var connected = await CheckConnectionAsync();

        if (connected)
        {
            await CheckAndApplyApprovedSwapsAsync();
        }
    }

    /// <summary>
    /// Проверить соединение прямым HTTP-запросом к узлу /messages.json.
    /// Пингуем тот же узел, к которому есть доступ по правилам Firebase.
    /// Каждый раз создаём новый HttpClient, чтобы не кешировать разорванное соединение.
    /// </summary>
    public async Task<bool> CheckConnectionAsync()
    {
        var currentUrl = Preferences.Get("FirebaseUrl", string.Empty).TrimEnd('/');

        if (string.IsNullOrEmpty(currentUrl))
        {
            Log("⚠️ URL не задан");
            UpdateStatus(false);
            return false;
        }

        if (_databaseUrl != currentUrl)
        {
            _databaseUrl = currentUrl;
        }

        try
        {
            // Пингуем /messages.json?shallow=true — лёгкий запрос к доступному узлу.
            // shallow=true возвращает только ключи, без тела сообщений.
            var url = $"{_databaseUrl}/messages.json?shallow=true";
            Log($"🔌 Ping: {url}");

            using var client = CreatePingClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await client.GetAsync(url, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ HTTP {(int)response.StatusCode}");
                UpdateStatus(true);
                return true;
            }

            Log($"❌ HTTP {(int)response.StatusCode}");
            UpdateStatus(false);
            return false;
        }
        catch (TaskCanceledException)
        {
            Log("⏱️ Timeout");
            UpdateStatus(false);
            return false;
        }
        catch (HttpRequestException ex)
        {
            Log($"🌐 Сетевая ошибка: {ex.Message}");
            UpdateStatus(false);
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка: {ex.Message}");
            UpdateStatus(false);
            return false;
        }
    }

    #region Swap Sync

    /// <summary>
    /// Проверить и применить все одобренные обмены
    /// </summary>
    public async Task CheckAndApplyApprovedSwapsAsync()
    {
        try
        {
            var employeeName = Preferences.Get("SelectedEmployee", string.Empty);

            if (string.IsNullOrEmpty(employeeName))
                return;

            var firebaseUrl = Preferences.Get("FirebaseUrl", string.Empty);
            if (string.IsNullOrEmpty(firebaseUrl))
                return;

            var swapService = new ShiftSwapService(firebaseUrl);
            var requests = await swapService.GetRequestsForEmployeeAsync(employeeName);

            var approvedSwaps = requests
                .Where(r => r.Status == "approved" && !IsSwapApplied(r.Id))
                .ToList();

            if (approvedSwaps.Count == 0)
                return;

            Log($"📋 Найдено {approvedSwaps.Count} новых одобренных обменов");

            foreach (var swap in approvedSwaps)
            {
                var success = await ApplySwapToLocalScheduleAsync(swap);
                if (success)
                {
                    Log($"✅ Обмен {swap.Id} применён");

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        SwapApplied?.Invoke(this, new SwapAppliedEventArgs(swap));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка проверки обменов: {ex.Message}");
        }
    }

    private static bool IsSwapApplied(string swapId)
    {
        return Preferences.Get($"swap_applied_{swapId}", false);
    }

    private async Task<bool> ApplySwapToLocalScheduleAsync(ShiftSwapRequest request)
    {
        try
        {
            Log($"=== Применяем обмен ===");
            Log($"{request.RequesterName} ({request.RequesterDate:dd.MM}, {request.RequesterShift})");
            Log($"{request.TargetName} ({request.TargetDate:dd.MM}, {request.TargetShift})");

            var filePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");

            if (!File.Exists(filePath))
            {
                Log("❌ Файл расписания не найден");
                return false;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var schedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json) ?? [];

            var requesterEntry = schedule.FirstOrDefault(e =>
                e.Employees.Equals(request.RequesterName, StringComparison.OrdinalIgnoreCase) &&
                e.Date.Date == request.RequesterDate.Date);

            var targetEntry = schedule.FirstOrDefault(e =>
                e.Employees.Equals(request.TargetName, StringComparison.OrdinalIgnoreCase) &&
                e.Date.Date == request.TargetDate.Date);

            if (requesterEntry == null || targetEntry == null)
            {
                Log("❌ Записи не найдены");
                Preferences.Set($"swap_applied_{request.Id}", true);
                return false;
            }

            // Меняем имена сотрудников местами
            requesterEntry.Employees = request.TargetName;
            targetEntry.Employees = request.RequesterName;

            Log($"После обмена:");
            Log($"{requesterEntry.Date:dd.MM}: {requesterEntry.Employees} ({requesterEntry.Shift})");
            Log($"{targetEntry.Date:dd.MM}: {targetEntry.Employees} ({targetEntry.Shift})");

            var updatedJson = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, updatedJson);

            Preferences.Set($"swap_applied_{request.Id}", true);

            Log("✅ Обмен применён и сохранён");
            return true;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка применения обмена: {ex.Message}");
            return false;
        }
    }

    #endregion

    private void UpdateStatus(bool connected)
    {
        var changed = _isConnected != connected;
        _isConnected = connected;

        if (changed)
        {
            Log($"📊 Статус: {(connected ? "🟢 ПОДКЛЮЧЕНО" : "🔴 ОТКЛЮЧЕНО")}");
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatusChanged?.Invoke(this, connected);
        });
    }

    private static void Log(string message)
    {
        var msg = $"[FirebaseMonitor] {message}";
        Trace.WriteLine(msg);
        Debug.WriteLine(msg);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Аргументы события применения обмена
/// </summary>
public class SwapAppliedEventArgs : EventArgs
{
    public ShiftSwapRequest Swap { get; }

    public SwapAppliedEventArgs(ShiftSwapRequest swap)
    {
        Swap = swap;
    }
}