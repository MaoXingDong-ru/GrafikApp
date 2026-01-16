using Grafik.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Grafik.Services
{
    /// <summary>
    /// Сервис для фонового опроса новых сообщений
    /// Работает независимо от открытого экрана
    /// </summary>
    public class BackgroundMessageService
    {
        private static BackgroundMessageService? _instance;
        private FirebaseService? _firebaseService;
        private CancellationTokenSource? _cancellationTokenSource;
        private DateTime _lastMessageTime = DateTime.MinValue;
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
        /// Приостановить полинг (когда приложение свернулось)
        /// </summary>
        public void Pause()
        {
            if (!_isRunning || _isPaused)
                return;

            _isPaused = true;
            Debug.WriteLine("[BackgroundMessageService] Полинг приостановлен (приложение в фоне)");
        }

        /// <summary>
        /// Возобновить полинг (когда приложение открылось)
        /// </summary>
        public void Resume()
        {
            if (!_isRunning || !_isPaused)
                return;

            _isPaused = false;
            Debug.WriteLine("[BackgroundMessageService] Полинг возобновлен (приложение открыто)");
        }

        /// <summary>
        /// Установить время последнего сообщения (для синхронизации с ChatPage)
        /// </summary>
        public void SetLastMessageTime(DateTime lastTime)
        {
            _lastMessageTime = lastTime;
            Debug.WriteLine($"[BackgroundMessageService] Время последнего сообщения установлено: {lastTime}");
        }

        /// <summary>
        /// Получить время последнего сообщения
        /// </summary>
        public DateTime GetLastMessageTime() => _lastMessageTime;

        /// <summary>
        /// Фоновый полинг сообщений
        /// </summary>
        private async Task PollMessagesBackgroundAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("[BackgroundMessageService] PollMessagesBackgroundAsync старт");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Опрашиваем каждые 3 секунды, но только если полинг не приостановлен
                    await Task.Delay(3000, cancellationToken);

                    // ❌ Если приложение в фоне - пропускаем полинг
                    if (_isPaused)
                    {
                        continue;
                    }

                    if (_firebaseService == null)
                        continue;

                    var newMessages = await _firebaseService.GetMessagesAfterAsync(_lastMessageTime);

                    if (newMessages.Count > 0)
                    {
                        Debug.WriteLine($"[BackgroundMessageService] Новых сообщений: {newMessages.Count}");

                        foreach (var msg in newMessages)
                        {
                            _lastMessageTime = msg.Timestamp;

                            // Вызываем событие о новом сообщении
                            NewMessageReceived?.Invoke(this, new NewMessageEventArgs
                            {
                                Message = msg,
                                SenderName = msg.Sender
                            });

                            Debug.WriteLine($"[BackgroundMessageService] Событие NewMessageReceived вызвано для {msg.Sender}");
                        }
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
                    // Продолжаем полинг при ошибке
                }
            }

            Debug.WriteLine("[BackgroundMessageService] PollMessagesBackgroundAsync завершён");
        }
    }
}
