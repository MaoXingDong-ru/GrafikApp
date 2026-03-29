#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Grafik.Services;

namespace Grafik;

[Service(
    ForegroundServiceType = ForegroundService.TypeDataSync,
    Exported = false)]
public class MessagePollingService : Android.App.Service
{
    private CancellationTokenSource? _cts;
    private FirebaseService? _firebaseService;
    private const int SERVICE_NOTIFICATION_ID = 9999;
    private const string CHANNEL_ID = "polling_service_channel";
    private bool _isFirstPoll = true;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        System.Diagnostics.Debug.WriteLine("[MessagePollingService] OnCreate");

        CreateServiceChannel();

        try
        {
            var notification = BuildServiceNotification();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(SERVICE_NOTIFICATION_ID, notification,
                    ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(SERVICE_NOTIFICATION_ID, notification);
            }

            System.Diagnostics.Debug.WriteLine("[MessagePollingService] ✅ StartForeground успешно");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MessagePollingService] ❌ StartForeground ошибка: {ex.Message}");
            StopSelf();
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        System.Diagnostics.Debug.WriteLine("[MessagePollingService] OnStartCommand");

        if (_cts != null && !_cts.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[MessagePollingService] Полинг уже запущен, пропускаем");
            return StartCommandResult.Sticky;
        }

        _cts = new CancellationTokenSource();

        var firebaseUrl = Preferences.Get("FirebaseUrl",
            "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");
        _firebaseService = new FirebaseService(firebaseUrl);

        _ = PollLoopAsync(_cts.Token);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        System.Diagnostics.Debug.WriteLine("[MessagePollingService] OnDestroy");
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnDestroy();
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        System.Diagnostics.Debug.WriteLine("[MessagePollingService] Цикл полинга запущен");

        while (!token.IsCancellationRequested)
        {
            try
            {
                int delayMs = App.IsAppInForeground ? 3_000 : 10_000;
                await Task.Delay(delayMs, token);

                if (_firebaseService == null)
                    continue;

                var unread = await _firebaseService.GetUnreadMessagesAsync();

                if (unread.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MessagePollingService] Непрочитанных: {unread.Count}, первый: {_isFirstPoll}");

                    if (_isFirstPoll)
                    {
                        await _firebaseService.MarkMessagesAsReadAsync(unread);
                        _isFirstPoll = false;
                        System.Diagnostics.Debug.WriteLine(
                            "[MessagePollingService] Первый полинг — все помечены как прочитанные");
                        continue;
                    }

                    foreach (var msg in unread)
                    {
                        if (!string.IsNullOrEmpty(msg.FirebaseKey))
                            await _firebaseService.MarkMessageAsReadAsync(msg.FirebaseKey);

                        if (App.IsAppInForeground && App.IsChatPageActive)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "[MessagePollingService] Чат открыт — пропускаем уведомление");
                            continue;
                        }

                        string preview = msg.Type switch
                        {
                            "file" => $"📎 {msg.FileName}",
                            "image" => $"🖼️ {msg.FileName ?? "Изображение"}",
                            _ => msg.Text.Length > 80 ? msg.Text[..80] + "..." : msg.Text
                        };

                        Services.NotificationService.ShowInstantNotification(
                            $"💬 {msg.Sender}",
                            preview,
                            Services.NotificationService.CHAT_CHANNEL_ID);

                        System.Diagnostics.Debug.WriteLine(
                            $"[MessagePollingService] 🔔 Уведомление: {msg.Sender}");
                    }
                }
                else if (_isFirstPoll)
                {
                    _isFirstPoll = false;
                }
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MessagePollingService] Ошибка: {ex.Message}");
                try { await Task.Delay(15_000, token); } catch { break; }
            }
        }

        System.Diagnostics.Debug.WriteLine("[MessagePollingService] Цикл полинга завершён");
    }

    private void CreateServiceChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        // Low вместо None — сервис не будет убит системой
        var channel = new NotificationChannel(
            CHANNEL_ID,
            "Синхронизация",
            NotificationImportance.Low) // ⚠️ Low, не None — иначе сервис может быть убит
        {
            Description = "Фоновая синхронизация сообщений"
        };
        channel.SetShowBadge(false);
        channel.EnableVibration(false);
        channel.EnableLights(false);
        channel.SetSound(null, null);
        channel.LockscreenVisibility = NotificationVisibility.Secret;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildServiceNotification()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

        var pendingIntent = PendingIntent.GetActivity(
            this, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Builder(this, CHANNEL_ID)
            .SetContentTitle("Grafik")
            .SetContentText("Синхронизация") // Короткий текст
            .SetSmallIcon(Android.Resource.Drawable.IcMenuInfoDetails)
            .SetOngoing(true)
            .SetContentIntent(pendingIntent)
            .SetPriority(NotificationCompat.PriorityMin) // Минимальный приоритет
            .SetCategory(Notification.CategoryService)
            .SetVisibility((int)NotificationVisibility.Secret) // Скрыть на lock screen
            .SetSilent(true)
            .Build();
    }
}
#endif