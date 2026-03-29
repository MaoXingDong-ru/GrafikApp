using System.Diagnostics;
using GrafikShared.Services;

namespace GrafikAdmin.Services;

/// <summary>
/// Синглтон для мониторинга соединения с Firebase и обновления состояний
/// </summary>
public sealed class FirebaseConnectionMonitor : IDisposable
{
    private static FirebaseConnectionMonitor? _instance;
    private static readonly object _lock = new();

    private Timer? _pollingTimer;
    private string _databaseUrl = string.Empty;
    private bool _isConnected;
    private bool _isStarted;

    /// <summary>
    /// Событие изменения статуса соединения
    /// </summary>
    public event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Событие обновления счётчика ожидающих запросов
    /// </summary>
    public event EventHandler<int>? PendingSwapsCountChanged;

    /// <summary>
    /// Текущий статус соединения
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Текущее количество ожидающих запросов
    /// </summary>
    public int PendingSwapsCount { get; private set; }

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
            UpdateConnectionStatus(false);
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

        Log("🚀 Мониторинг запущен (интервал: 10 сек)");
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
    /// Один тик polling — проверяем соединение и обновляем все состояния
    /// </summary>
    private async Task PollingTickAsync()
    {
        // 1. Проверяем соединение
        var connected = await CheckConnectionAsync();

        // 2. Если соединение есть — обновляем счётчик ожидающих запросов
        if (connected)
        {
            await UpdatePendingSwapsCountAsync();
        }
    }

    /// <summary>
    /// Проверить соединение
    /// </summary>
    public async Task<bool> CheckConnectionAsync()
    {
        var currentUrl = Preferences.Get("FirebaseUrl", string.Empty).TrimEnd('/');

        if (string.IsNullOrEmpty(currentUrl))
        {
            UpdateConnectionStatus(false);
            return false;
        }

        if (_databaseUrl != currentUrl)
        {
            _databaseUrl = currentUrl;
        }

        try
        {
            var service = new ShiftSwapService(_databaseUrl);
            var requests = await service.GetAllRequestsAsync();

            UpdateConnectionStatus(true);
            return true;
        }
        catch (TaskCanceledException)
        {
            Log("⏱️ Timeout");
            UpdateConnectionStatus(false);
            return false;
        }
        catch (HttpRequestException ex)
        {
            Log($"🌐 Сетевая ошибка: {ex.Message}");
            UpdateConnectionStatus(false);
            return false;
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка: {ex.Message}");
            UpdateConnectionStatus(false);
            return false;
        }
    }

    /// <summary>
    /// Обновить счётчик ожидающих запросов на обмен
    /// </summary>
    private async Task UpdatePendingSwapsCountAsync()
    {
        try
        {
            var service = new ShiftSwapService(_databaseUrl);
            var pendingRequests = await service.GetPendingRequestsAsync();
            var count = pendingRequests.Count;

            if (PendingSwapsCount != count)
            {
                PendingSwapsCount = count;
                Log($"📋 Ожидающих запросов: {count}");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PendingSwapsCountChanged?.Invoke(this, count);
                });
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Ошибка загрузки счётчика: {ex.Message}");
        }
    }

    private void UpdateConnectionStatus(bool connected)
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