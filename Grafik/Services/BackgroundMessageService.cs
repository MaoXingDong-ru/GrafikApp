using Grafik.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Grafik.Services
{
    /// <summary>
    /// Сервис для фонового опроса новых сообщений.
    /// Использует поле readBy в Firebase для определения непрочитанных сообщений на каждом устройстве.
    /// </summary>
    public class BackgroundMessageService
    {
        private static BackgroundMessageService? _instance;
        private FirebaseService? _firebaseService;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning = false;
        private bool _isPaused = false;

        public static BackgroundMessageService Instance => _instance ??= new BackgroundMessageService();

        /// <summary>
        /// Событие для уведомления когда пришло новое сообщение
        /// </summary>
        public event EventHandler<NewMessageEventArgs>? NewMessageReceived;

        public class NewMessageEventArgs : EventArgs
        {
            public FirebaseMessage Message { get; set; } = null!;
            public string SenderName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Запустить фоновый полинг
        /// </summary>
        public void Start(string firebaseUrl)
        {
            if (_isRunning)
            {
                Debug.WriteLine("[BackgroundMessageService] Сервис уже запущен");
                return;
            }

            try
            {
                Debug.WriteLine("[BackgroundMessageService] Запуск сервиса фонового опроса сообщений");
                Debug.WriteLine($"[BackgroundMessageService] DeviceId: {FirebaseService.GetDeviceId()}");
                
                _firebaseService = new FirebaseService(firebaseUrl);
                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;

                // Запускаем полинг в фоне
                _ = PollMessagesBackgroundAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackgroundMessageService] Ошибка при запуске: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Остановить фоновый полинг
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            try
            {
                Debug.WriteLine("[BackgroundMessageService] Остановка сервиса");
                
                if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }

                _isRunning = false;
                _isPaused = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackgroundMessageService] Ошибка при остановке: {ex.Message}");
            }
        }

        /// <summary>
        /// Приостановить полинг (когда приложение свернулось — увеличиваем интервал)
        /// </summary>
        public void Pause()
        {
            if (!_isRunning || _isPaused)
                return;

            _isPaused = true;
            Debug.WriteLine("[BackgroundMessageService] Приложение в фоне — полинг с увеличенным интервалом");
        }

        /// <summary>
        /// Возобновить полинг (когда приложение открылось)
        /// </summary>
        public void Resume()
        {
            if (!_isRunning || !_isPaused)
                return;

            _isPaused = false;
            Debug.WriteLine("[BackgroundMessageService] Полинг возобновлен с обычным интервалом");
        }

        /// <summary>
        /// Пометить все сообщения как прочитанные на текущем устройстве
        /// (вызывается когда пользователь открывает чат)
        /// </summary>
        public async Task MarkAllAsReadAsync()
        {
            if (_firebaseService == null)
                return;

            try
            {
                var unread = await _firebaseService.GetUnreadMessagesAsync();
                if (unread.Count > 0)
                {
                    Debug.WriteLine($"[BackgroundMessageService] Помечаем {unread.Count} сообщений как прочитанные");
                    await _firebaseService.MarkMessagesAsReadAsync(unread);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackgroundMessageService] Ошибка MarkAllAsReadAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Фоновый полинг сообщений — проверяет readBy для текущего устройства
        /// </summary>
        private async Task PollMessagesBackgroundAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("[BackgroundMessageService] PollMessagesBackgroundAsync старт");

            // Первый запуск — помечаем все как прочитанные, чтобы не спамить уведомлениями
            bool isFirstPoll = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // В фоне опрашиваем реже (10 сек), на переднем плане — каждые 3 сек
                    int delayMs = _isPaused ? 10000 : 3000;
                    await Task.Delay(delayMs, cancellationToken);

                    if (_firebaseService == null)
                        continue;

                    // Получаем сообщения, которые ещё не прочитаны этим устройством
                    var unreadMessages = await _firebaseService.GetUnreadMessagesAsync();

                    if (unreadMessages.Count > 0)
                    {
                        Debug.WriteLine($"[BackgroundMessageService] Непрочитанных: {unreadMessages.Count}, первый полинг: {isFirstPoll}");

                        if (isFirstPoll)
                        {
                            // При первом запуске просто помечаем все как прочитанные
                            await _firebaseService.MarkMessagesAsReadAsync(unreadMessages);
                            isFirstPoll = false;
                            Debug.WriteLine("[BackgroundMessageService] Первый полинг — все помечены как прочитанные");
                            continue;
                        }

                        foreach (var msg in unreadMessages)
                        {
                            // Помечаем как прочитанное в Firebase
                            if (!string.IsNullOrEmpty(msg.FirebaseKey))
                            {
                                await _firebaseService.MarkMessageAsReadAsync(msg.FirebaseKey);
                            }

                            // Вызываем событие о новом сообщении
                            NewMessageReceived?.Invoke(this, new NewMessageEventArgs
                            {
                                Message = msg,
                                SenderName = msg.Sender
                            });

                            Debug.WriteLine($"[BackgroundMessageService] 🔔 Новое сообщение от {msg.Sender}");
                        }
                    }
                    else if (isFirstPoll)
                    {
                        isFirstPoll = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[BackgroundMessageService] Полинг отменён");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackgroundMessageService] Ошибка полинга: {ex.Message}");
                }
            }

            Debug.WriteLine("[BackgroundMessageService] PollMessagesBackgroundAsync завершён");
        }
    }
}
