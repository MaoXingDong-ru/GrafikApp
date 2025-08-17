using System;
using System.Threading.Tasks;

#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;
#elif WINDOWS
using Microsoft.Maui.Controls;
#endif

namespace Grafik.Services
{
    public static class NotificationService
    {
        // Универсальный метод планирования уведомления
        public static void ScheduleNotification(string title, string message, DateTime notifyTime)
        {
            // Если время уже прошло, ничего не делаем
            if (notifyTime <= DateTime.Now) return;

#if ANDROID
            var context = Android.App.Application.Context;
            long triggerMillis = (long)(notifyTime - DateTime.Now).TotalMilliseconds;

            var intent = new Intent(context, typeof(AlarmReceiver));
            intent.PutExtra("title", title);
            intent.PutExtra("message", message);

            var pendingIntent = PendingIntent.GetBroadcast(
                context,
                0,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);
            alarmManager.SetExactAndAllowWhileIdle(
                AlarmType.RtcWakeup,
                Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis,
                pendingIntent);

#elif WINDOWS
            Task.Run(async () =>
            {
                var delay = notifyTime - DateTime.Now;
                if (delay.TotalMilliseconds <= 0) return;
                await Task.Delay(delay);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage.DisplayAlert(title, message, "OK");
                });
            });
#endif
        }

        // Новый метод: ставим уведомление напрямую из ShiftEntry
        public static void ScheduleShiftNotification(ShiftEntry entry, ReminderOption reminder)
        {
            if (string.IsNullOrWhiteSpace(entry.Worktime)) return;

            try
            {
                // Разделяем начало и конец смены
                var parts = entry.Worktime.Split('-');
                if (parts.Length < 1) return;

                // Парсим время начала смены
                var startTime = TimeSpan.Parse(parts[0].Trim());
                DateTime shiftStart = entry.Date.Date + startTime;

                // Вычисляем время уведомления
                DateTime notifyTime = ReminderHelper.GetNotificationTime(shiftStart, reminder);

                // Не ставим уведомление для прошедших смен
                if (notifyTime <= DateTime.Now) return;

                // Ставим уведомление
                ScheduleNotification(
                    $"Смена: {entry.Shift}",
                    $"Скоро смена у {entry.Employees}",
                    notifyTime);
            }
            catch
            {
                // Игнорируем ошибки парсинга времени
            }
        }
    }

    public enum ReminderOption
    {
        Minutes15,
        Hour1,
        Hours12
    }

    public static class ReminderHelper
    {
        public static TimeSpan ToTimeSpan(this ReminderOption reminder)
        {
            return reminder switch
            {
                ReminderOption.Minutes15 => TimeSpan.FromMinutes(15),
                ReminderOption.Hour1 => TimeSpan.FromHours(1),
                ReminderOption.Hours12 => TimeSpan.FromHours(12),
                _ => TimeSpan.Zero
            };
        }

        public static DateTime GetNotificationTime(DateTime shiftStart, ReminderOption reminder)
        {
            return shiftStart - reminder.ToTimeSpan();
        }
    }

#if ANDROID
    [BroadcastReceiver(Enabled = true, Exported = true)]
    public class AlarmReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            string title = intent.GetStringExtra("title") ?? "Напоминание";
            string message = intent.GetStringExtra("message") ?? "Скоро смена!";

            var notificationBuilder = new NotificationCompat.Builder(context, "default")
                .SetContentTitle(title)
                .SetContentText(message)
                .SetAutoCancel(true)
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);

            var notificationManager = NotificationManagerCompat.From(context);
            notificationManager.Notify(1, notificationBuilder.Build());
        }
    }
#endif
}
