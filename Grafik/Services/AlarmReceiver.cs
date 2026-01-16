#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace Grafik.Services
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, "android.intent.action.ALARM" })]
    public class AlarmReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (context == null || intent == null)
            {
                System.Diagnostics.Debug.WriteLine("[AlarmReceiver] ❌ Context или Intent = null");
                return;
            }

            string title = intent?.GetStringExtra("title") ?? "Напоминание";
            string message = intent?.GetStringExtra("message") ?? "Скоро смена!";
            string channelId = intent?.GetStringExtra("channel_id") ?? NotificationService.SHIFT_CHANNEL_ID;
            int notificationId = intent?.GetIntExtra("notification_id", new System.Random().Next(1000, 9999)) 
                ?? new System.Random().Next(1000, 9999);

            System.Diagnostics.Debug.WriteLine($"[AlarmReceiver] 🔔 Будильник сработал!");
            System.Diagnostics.Debug.WriteLine($"[AlarmReceiver]   ID: {notificationId}");
            System.Diagnostics.Debug.WriteLine($"[AlarmReceiver]   Название: {title}");
            System.Diagnostics.Debug.WriteLine($"[AlarmReceiver]   Канал: {channelId}");

            try
            {
                NotificationService.CreateNotificationChannels();

                var notificationBuilder = new NotificationCompat.Builder(context, channelId)
                    .SetContentTitle(title)
                    .SetContentText(message)
                    .SetAutoCancel(true)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(message));

                // Разные приоритеты для разных типов
                if (channelId == NotificationService.SHIFT_CHANNEL_ID)
                {
                    notificationBuilder
                        .SetPriority(NotificationCompat.PriorityHigh)
                        .SetCategory(NotificationCompat.CategoryReminder)
                        .SetVibrate(new long[] { 0, 250, 250, 250 });
                }
                else
                {
                    notificationBuilder.SetPriority(NotificationCompat.PriorityDefault);
                }

                var notificationManager = NotificationManagerCompat.From(context);
                notificationManager.Notify(notificationId, notificationBuilder.Build());

                System.Diagnostics.Debug.WriteLine($"[AlarmReceiver] ✅ Уведомление отправлено: {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlarmReceiver] ❌ Ошибка: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AlarmReceiver] Stack: {ex.StackTrace}");
            }
        }
    }
}
#endif