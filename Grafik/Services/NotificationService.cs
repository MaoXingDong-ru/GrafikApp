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
            if (notifyTime <= DateTime.Now)
            {
                Console.WriteLine($"Время уведомления в прошлом: {notifyTime}");
                return;
            }

#if ANDROID
            try
            {
                CreateNotificationChannel();
                
                var context = Android.App.Application.Context;
                
                long triggerMillis = (long)(notifyTime - DateTime.Now).TotalMilliseconds;

                // Используем хеш для создания уникального requestCode
                int notificationId = (title + message + notifyTime.Ticks).GetHashCode() & 0x7FFFFFFF;

                var intent = new Intent(context, typeof(AlarmReceiver));
                intent.SetAction("android.intent.action.ALARM");
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
                    long triggerAtMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis;
                    
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    {
                        // Android 12+
                        if (alarmManager.CanScheduleExactAlarms())
                        {
                            alarmManager.SetExactAndAllowWhileIdle(
                                AlarmType.RtcWakeup,
                                triggerAtMillis,
                                pendingIntent);
                        }
                        else
                        {
                            alarmManager.SetAndAllowWhileIdle(
                                AlarmType.RtcWakeup,
                                triggerAtMillis,
                                pendingIntent);
                        }
                    }
                    else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        // Android 6+
                        alarmManager.SetExactAndAllowWhileIdle(
                            AlarmType.RtcWakeup,
                            triggerAtMillis,
                            pendingIntent);
                    }
                    else if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                    {
                        // Android 4.4+
                        alarmManager.SetExact(
                            AlarmType.RtcWakeup,
                            triggerAtMillis,
                            pendingIntent);
                    }
                    else
                    {
                        // Older Android
                        alarmManager.Set(
                            AlarmType.RtcWakeup,
                            triggerAtMillis,
                            pendingIntent);
                    }
                    
                    Console.WriteLine($"Уведомление запланировано: {title} на {notifyTime} (через {triggerMillis}ms)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка планирования уведомления: {ex.Message}\n{ex.StackTrace}");
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
                var timeParts = entry.Worktime.Split('-');
                if (timeParts.Length == 0) return;

                string startTimeString = timeParts[0].Trim();

                if (TryParseTime(startTimeString, out TimeSpan startTime))
                {
                    DateTime shiftStart = entry.Date.Date + startTime;

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
            try
            {
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
                
                Console.WriteLine("Мгновенное уведомление отправлено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке уведомления: {ex.Message}");
            }
#endif
        }

        public static void ScheduleTestNotification()
        {
#if ANDROID
            try
            {
                DateTime testTime = DateTime.Now.AddSeconds(10);
                
                ScheduleNotification(
                    "Тестовое уведомление",
                    "Проверка планирования уведомлений",
                    testTime);
                
                Console.WriteLine($"Тестовое уведомление запланировано на {testTime}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при планировании уведомления: {ex.Message}");
            }
#endif
        }
    }
}