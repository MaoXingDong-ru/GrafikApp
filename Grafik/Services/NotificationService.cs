using System;
using System.Threading.Tasks;

#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Android.OS;
#elif WINDOWS
using Microsoft.Maui.Controls;
#endif

namespace Grafik.Services
{
    public static class NotificationService
    {
        public const string CHANNEL_ID = "shift_reminder_channel";
        private const string CHANNEL_NAME = "Напоминания о сменах";
        private const string CHANNEL_DESCRIPTION = "Уведомления о предстоящих сменах";

#if ANDROID
        public static void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var channel = new NotificationChannel(CHANNEL_ID, CHANNEL_NAME, 
                NotificationImportance.High)
            {
                Description = CHANNEL_DESCRIPTION
            };

            var notificationManager = Android.App.Application.Context
                .GetSystemService(Context.NotificationService) as NotificationManager;
            
            notificationManager?.CreateNotificationChannel(channel);
        }
#endif

        public static void ScheduleNotification(string title, string message, DateTime notifyTime)
        {
            if (notifyTime <= DateTime.Now) return;

#if ANDROID
            CreateNotificationChannel();
            
            var context = Android.App.Application.Context;
            
            DateTime utcNotifyTime = notifyTime.ToUniversalTime();
            long triggerMillis = (long)(utcNotifyTime - DateTime.UtcNow).TotalMilliseconds;

            int notificationId = new Random().Next(1000, 9999);

            var intent = new Intent(context, typeof(AlarmReceiver));
            intent.PutExtra("title", title);
            intent.PutExtra("message", message);
            intent.PutExtra("notification_id", notificationId);

            var pendingIntent = PendingIntent.GetBroadcast(
                context,
                notificationId,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);
            
            if (alarmManager != null)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                {
                    alarmManager.SetExactAndAllowWhileIdle(
                        AlarmType.RtcWakeup,
                        Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis,
                        pendingIntent);
                }
                else if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                {
                    alarmManager.SetExact(
                        AlarmType.RtcWakeup,
                        Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis,
                        pendingIntent);
                }
                else
                {
                    alarmManager.Set(
                        AlarmType.RtcWakeup,
                        Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis,
                        pendingIntent);
                }
            }

#elif WINDOWS
            ScheduleLocalNotification(title, message, notifyTime);
#endif
        }

#if WINDOWS
        private static async void ScheduleLocalNotification(string title, string message, DateTime notifyTime)
        {
            try
            {
                var delay = notifyTime - DateTime.Now;
                if (delay.TotalMilliseconds <= 0) return;
                
                await Task.Delay(delay);
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current?.MainPage?.DisplayAlert(title, message, "OK");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка Windows уведомления: {ex.Message}");
            }
        }
#endif

        public static void ScheduleShiftNotification(ShiftEntry entry, ReminderOption reminder)
        {
            if (string.IsNullOrWhiteSpace(entry.Worktime) || string.IsNullOrWhiteSpace(entry.Shift))
                return;

            try
            {
                // Более надежный парсинг времени
                var timeParts = entry.Worktime.Split('-');
                if (timeParts.Length == 0) return;

                string startTimeString = timeParts[0].Trim();

                // Пробуем разные форматы времени
                if (TryParseTime(startTimeString, out TimeSpan startTime))
                {
                    DateTime shiftStart = entry.Date.Date + startTime;

                    // Проверяем, что смена в будущем (с запасом +1 день на случай ночных смен)
                    if (shiftStart <= DateTime.Now.AddDays(-1))
                        return;

                    DateTime notifyTime = ReminderHelper.GetNotificationTime(shiftStart, reminder);

                    // Дополнительная проверка, что уведомление в будущем
                    if (notifyTime > DateTime.Now)
                    {
                        string notificationMessage = entry.Shift.ToLower() switch
                        {
                            string s when s.Contains("ноч") => $"{entry.Employees}, ночная смена начинается в {startTime:hh\\:mm}",
                            string s when s.Contains("днев") => $"{entry.Employees}, дневная смена начинается в {startTime:hh\\:mm}",
                            _ => $"{entry.Employees}, смена начинается в {startTime:hh\\:mm}"
                        };

                        ScheduleNotification(
                            $"Смена: {entry.Shift}",
                            notificationMessage,
                            notifyTime);

                        Console.WriteLine($"Запланировано уведомление для {entry.Employees} на {notifyTime} (смена: {shiftStart})");
                    }
                }
                else
                {
                    Console.WriteLine($"Не удалось распарсить время: {startTimeString} для {entry.Employees}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка планирования уведомления для {entry.Employees}: {ex.Message}");
            }
        }

        // Вспомогательный метод для парсинга времени
        private static bool TryParseTime(string timeString, out TimeSpan result)
        {
            result = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(timeString))
                return false;

            // Пробуем разные форматы
            string[] formats = { "hh\\:mm", "h\\:mm", "hhmm", "hmm" };

            foreach (var format in formats)
            {
                if (TimeSpan.TryParseExact(timeString, format, null, out result))
                    return true;
            }

            // Пробуем стандартный парсинг
            return TimeSpan.TryParse(timeString, out result);
        }

        // Метод для отладки - немедленное тестовое уведомление
        public static void ShowTestNotification()
        {
#if ANDROID
    CreateNotificationChannel();
    
    var context = Android.App.Application.Context;
    int notificationId = new Random().Next(1000, 9999);

    var notificationBuilder = new NotificationCompat.Builder(context, CHANNEL_ID)
        .SetContentTitle("Тестовое уведомление")
        .SetContentText("Проверка работы уведомлений")
        .SetAutoCancel(true)
        .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
        .SetPriority(NotificationCompat.PriorityHigh);

    var notificationManager = NotificationManagerCompat.From(context);
    notificationManager.Notify(notificationId, notificationBuilder.Build());
#endif
        }
        public static void ScheduleTestNotification()
        {
#if ANDROID
    // Планируем уведомление через 10 секунд
    DateTime testTime = DateTime.Now.AddSeconds(10);
    
    ScheduleNotification(
        "Тестовое уведомление",
        "Проверка планирования уведомлений",
        testTime);
    
    Console.WriteLine($"Тестовое уведомление запланировано на {testTime}");
#endif
        }
    }

}