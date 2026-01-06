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
                return;

            string title = intent?.GetStringExtra("title") ?? "Напоминание";
            string message = intent?.GetStringExtra("message") ?? "Скоро смена!";
            int notificationId = intent?.GetIntExtra("notification_id", new System.Random().Next(1000, 9999)) ?? new System.Random().Next(1000, 9999);

            try
            {
                CreateNotificationChannel(context);

                var notificationBuilder = new NotificationCompat.Builder(context, NotificationService.CHANNEL_ID)
                    .SetContentTitle(title)
                    .SetContentText(message)
                    .SetAutoCancel(true)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetCategory(NotificationCompat.CategoryReminder);

                var notificationManager = NotificationManagerCompat.From(context);
                notificationManager.Notify(notificationId, notificationBuilder.Build());
                
                System.Diagnostics.Debug.WriteLine($"Уведомление отправлено: {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка AlarmReceiver: {ex.Message}");
            }
        }

        private void CreateNotificationChannel(Context context)
        {
            if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.O)
                return;

            var channel = new NotificationChannel(
                NotificationService.CHANNEL_ID,
                "Напоминания о сменах",
                NotificationImportance.High)
            {
                Description = "Уведомления о предстоящих сменах"
            };

            var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            notificationManager?.CreateNotificationChannel(channel);
        }
    }
}
#endif