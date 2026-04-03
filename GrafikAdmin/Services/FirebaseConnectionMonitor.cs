using System.Diagnostics;
using GrafikShared.Services;

namespace GrafikAdmin.Services;


/// Синглтон для мониторинга соединения с Firebase и обновления состояний

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
        MaxConnectionsPerServer = 1
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };


    // Событие изменения статуса соединения

    public event EventHandler<bool>? ConnectionStatusChanged;


    // Событие обновления счётчика ожидающих запросов

    public event EventHandler<int>? PendingSwapsCountChanged;


    // Текущий статус соединения

    public bool IsConnected => _isConnected;


    // Текущее количество ожидающих запросов

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
            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            Log("▶️ Мониторинг возобновлён");
        }
    }


    // Один тик polling — проверяем соединение и обновляем все состояния

    private async Task PollingTickAsync()
    {
        var connected = await CheckConnectionAsync();

        if (connected)
        {
            await UpdatePendingSwapsCountAsync();
        }
    }


    // Проверить соединение прямым HTTP-запросом к узлу /swapRequests.json.
    // Пингуем тот же узел, к которому есть доступ по правилам Firebase.
    // Каждый раз создаём новый HttpClient, чтобы не кешировать разорванное соединение.

    public async Task<bool> CheckConnectionAsync()
    {
        var currentUrl = Preferences.Get("FirebaseUrl", string.Empty).TrimEnd('/');

        if (string.IsNullOrEmpty(currentUrl))
        {
            Log("⚠️ URL не задан");
            UpdateConnectionStatus(false);
            return false;
        }

        if (_databaseUrl != currentUrl)
        {
            _databaseUrl = currentUrl;
        }

        try
        {
            // Пингуем /swapRequests.json?shallow=true — лёгкий запрос к доступному узлу.
            // shallow=true возвращает только ключи, без данных.
            var url = $"{_databaseUrl}/swapRequests.json?shallow=true";
            Log($"🔌 Ping: {url}");

            using var client = CreatePingClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await client.GetAsync(url, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                Log($"✅ HTTP {(int)response.StatusCode}");
                UpdateConnectionStatus(true);
                return true;
            }

            Log($"❌ HTTP {(int)response.StatusCode}");
            UpdateConnectionStatus(false);
            return false;
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


    // Обновить счётчик ожидающих запросов на обмен

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