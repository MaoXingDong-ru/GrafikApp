using System.Diagnostics;
using System.Text.Json;
using GrafikShared.Services;

namespace Grafik.Services;


/// Синглтон для мониторинга соединения с Firebase и синхронизации обменов ///
/// Бюджет трафика (Firebase Spark: 360 МБ/сутки на всю базу, ~25 пользователей): ///
///   — Ping (HEAD) раз в 60 сек:       ~200 байт × 1440 = ~0.3 МБ / польз / сутки ///
///   — Проверка обменов раз в 5 мин:   ~1 КБ    × 288  = ~0.3 МБ / польз / сутки ///
///   — Итого на 25 пользователей:      ≈ 15 МБ/сутки (4% от лимита) ///

public sealed class FirebaseConnectionMonitor : IDisposable
{
    private static FirebaseConnectionMonitor? _instance;
    private static readonly object _lock = new();

    private Timer? _pollingTimer;
    private string _databaseUrl = string.Empty;
    private bool _isConnected;
    private bool _isStarted;


    // Интервал ping-проверки соединения (индикатор).
    // 60 сек — достаточно для отображения статуса, не перегружает лимит.

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(60);


    // Интервал проверки одобренных обменов.
    // 5 минут — компромисс между актуальностью и экономией трафика.

    private static readonly TimeSpan SwapCheckInterval = TimeSpan.FromMinutes(5);


    // Счётчик тиков для вычисления момента проверки обменов.

    private int _tickCount;

    // Не используем статический HttpClient — пересоздаём при каждой проверке,
    // чтобы избежать кеширования разорванного соединения
    private static HttpClient CreatePingClient() => new(new HttpClientHandler
    {
        // Отключаем keep-alive для ping — каждый запрос создаёт новое соединение
        MaxConnectionsPerServer = 1
    })
    {
        // HEAD-запрос не возвращает тело — минимальный трафик
        Timeout = TimeSpan.FromSeconds(8)
    };


    // Событие изменения статуса соединения

    public event EventHandler<bool>? ConnectionStatusChanged;


    // Событие при применении нового обмена (для обновления UI)

    public event EventHandler<SwapAppliedEventArgs>? SwapApplied;


    // Текущий статус соединения

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


    // Запустить мониторинг

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

        _tickCount = 0;
        _isStarted = true;

        // Первый тик — сразу проверяем соединение
        _ = PollingTickAsync();

        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(
            async _ => await PollingTickAsync(),
            null,
            PingInterval,
            PingInterval
        );

        Log($"🚀 Мониторинг запущен (ping: {PingInterval.TotalSeconds}с, обмены: {SwapCheckInterval.TotalMinutes}мин)");
    }


    // Остановить мониторинг

    public void Stop()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isStarted = false;
        Log("⏹️ Мониторинг остановлен");
    }


    // Перезапустить мониторинг (при смене URL)

    public void Restart()
    {
        Stop();
        Start();
    }


    // Приостановить polling (приложение свёрнуто)

    public void Pause()
    {
        _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        Log("⏸️ Мониторинг приостановлен");
    }


    // Возобновить polling (приложение активно)

    public void Resume()
    {
        if (_isStarted && _pollingTimer != null)
        {
            _pollingTimer.Change(TimeSpan.Zero, PingInterval);
            Log("▶️ Мониторинг возобновлён");
        }
    }


    // Один тик: ///
    //   — каждый тик (60 сек): HEAD-ping для обновления индикатора ///
    //   — каждые N тиков (5 мин): дополнительно проверяем одобренные обмены ///

    private async Task PollingTickAsync()
    {
        _tickCount++;

        var connected = await CheckConnectionAsync();

        if (!connected)
            return;

        // Вычисляем сколько тиков соответствует интервалу проверки обменов
        var ticksPerSwapCheck = (int)(SwapCheckInterval.TotalSeconds / PingInterval.TotalSeconds);

        if (_tickCount % ticksPerSwapCheck == 0)
        {
            Log("🔄 Проверка одобренных обменов...");
            await CheckAndApplyApprovedSwapsAsync();
        }
    }


    // Проверить соединение HEAD-запросом — минимальный трафик (нет тела ответа).
    // Каждый раз создаём новый HttpClient, чтобы не кешировать разорванное соединение.

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
            // GET к /messages.json,
            // трафик ~500 байт при GET?shallow=true
            var url = $"{_databaseUrl}/messages.json?shallow=true";
            Log($"🔌 Ping: {url}");

            using var client = CreatePingClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await client.SendAsync(request, cts.Token);

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


    // Проверить и применить все одобренные обмены

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


/// Аргументы события применения обмена

public class SwapAppliedEventArgs : EventArgs
{
    public ShiftSwapRequest Swap { get; }

    public SwapAppliedEventArgs(ShiftSwapRequest swap)
    {
        Swap = swap;
    }
}