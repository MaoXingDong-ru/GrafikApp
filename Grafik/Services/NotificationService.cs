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
        public const string SHIFT_CHANNEL_ID = "shift_reminder_channel";
        public const string CHAT_CHANNEL_ID = "chat_messages_channel";
        
        private const string SHIFT_CHANNEL_NAME = "Напоминания о сменах";
        private const string SHIFT_CHANNEL_DESCRIPTION = "Уведомления о предстоящих сменах";
        private const string CHAT_CHANNEL_NAME = "Сообщения чата";
        private const string CHAT_CHANNEL_DESCRIPTION = "Уведомления о новых сообщениях в чате";

#if ANDROID
        public static void CreateNotificationChannels()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var notificationManager = Android.App.Application.Context
                .GetSystemService(Context.NotificationService) as NotificationManager;

            if (notificationManager == null)
                return;

            // Канал для напоминаний о сменах
            var shiftChannel = new NotificationChannel(SHIFT_CHANNEL_ID, SHIFT_CHANNEL_NAME, 
                NotificationImportance.High)
            {
                Description = SHIFT_CHANNEL_DESCRIPTION
            };
            shiftChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
            shiftChannel.SetShowBadge(true);
            notificationManager.CreateNotificationChannel(shiftChannel);

            // Канал для сообщений чата
            var chatChannel = new NotificationChannel(CHAT_CHANNEL_ID, CHAT_CHANNEL_NAME,
                NotificationImportance.High)
            {
                Description = CHAT_CHANNEL_DESCRIPTION
            };
            chatChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
            chatChannel.SetShowBadge(true);
            notificationManager.CreateNotificationChannel(chatChannel);

            System.Diagnostics.Debug.WriteLine("[NotificationService] Каналы уведомлений созданы");
        }
#endif

        /// <summary>
        /// Показать немедленное уведомление (для новых сообщений в чате)
        /// </summary>
        public static void ShowInstantNotification(string title, string message, string channelId = CHAT_CHANNEL_ID)
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                int notificationId = (title + message + DateTime.Now.Ticks).GetHashCode() & 0x7FFFFFFF;

                var notificationBuilder = new NotificationCompat.Builder(context, channelId)
                    .SetContentTitle(title)
                    .SetContentText(message)
                    .SetAutoCancel(true)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(message))
                    .SetVibrate(new long[] { 0, 250, 250, 250 });

                var notificationManager = NotificationManagerCompat.From(context);
                notificationManager.Notify(notificationId, notificationBuilder.Build());

                System.Diagnostics.Debug.WriteLine($"[NotificationService] Системное уведомление отправлено: {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Ошибка: {ex.Message}");
            }

#elif WINDOWS
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Application.Current?.MainPage?.DisplayAlert(title, message, "OK");
            });
#endif
        }

        /// <summary>
        /// Запланировать уведомление на определённое время
        /// </summary>
        public static void ScheduleNotification(string title, string message, DateTime notifyTime, 
            string channelId = SHIFT_CHANNEL_ID)
        {
            if (notifyTime <= DateTime.Now)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Время в прошлом: {notifyTime} (сейчас: {DateTime.Now})");
                return;
            }

#if ANDROID
            try
            {
                CreateNotificationChannels();

                var context = Android.App.Application.Context;
                
                long triggerMillis = (long)(notifyTime - DateTime.Now).TotalMilliseconds;
                int notificationId = (title + message + notifyTime.Ticks).GetHashCode() & 0x7FFFFFFF;

                System.Diagnostics.Debug.WriteLine($"[NotificationService] 📅 Планирование уведомления:");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   ID: {notificationId}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Название: {title}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Сообщение: {message}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Время: {notifyTime:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Через: {triggerMillis}ms ({triggerMillis / 1000}сек)");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Канал: {channelId}");

                var intent = new Intent(context, typeof(AlarmReceiver));
                intent.SetAction("android.intent.action.ALARM");
                intent.PutExtra("title", title);
                intent.PutExtra("message", message);
                intent.PutExtra("notification_id", notificationId);
                intent.PutExtra("channel_id", channelId);

                var pendingIntent = PendingIntent.GetBroadcast(
                    context,
                    notificationId,
                    intent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);

                if (alarmManager != null)
                {
                    long triggerAtMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + triggerMillis;

                    try
                    {
                        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                        {
                            if (alarmManager.CanScheduleExactAlarms())
                            {
                                alarmManager.SetExactAndAllowWhileIdle(
                                    AlarmType.RtcWakeup,
                                    triggerAtMillis,
                                    pendingIntent);
                                System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ SetExactAndAllowWhileIdle использован (Android 12+)");
                            }
                            else
                            {
                                alarmManager.SetAndAllowWhileIdle(
                                    AlarmType.RtcWakeup,
                                    triggerAtMillis,
                                    pendingIntent);
                                System.Diagnostics.Debug.WriteLine($"[NotificationService] ⚠️ SetAndAllowWhileIdle использован (нет разрешения SCHEDULE_EXACT_ALARM)");
                            }
                        }
                        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                        {
                            alarmManager.SetExactAndAllowWhileIdle(
                                AlarmType.RtcWakeup,
                                triggerAtMillis,
                                pendingIntent);
                            System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ SetExactAndAllowWhileIdle использован (Android 6-11)");
                        }
                        else if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
                        {
                            alarmManager.SetExact(
                                AlarmType.RtcWakeup,
                                triggerAtMillis,
                                pendingIntent);
                            System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ SetExact использован (Android 4.4-5.x)");
                        }
                        else
                        {
                            alarmManager.Set(
                                AlarmType.RtcWakeup,
                                triggerAtMillis,
                                pendingIntent);
                            System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Set использован (старые версии)");
                        }

                        System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Уведомление успешно запланировано!");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка при установке будильника: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ AlarmManager не доступен!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка планирования: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Stack: {ex.StackTrace}");
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
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка Windows: {ex.Message}");
            }
        }
#endif

        /// <summary>
        /// Запланировать уведомление о смене
        /// </summary>
        public static void ScheduleShiftNotification(ShiftEntry entry, ReminderOption reminder)
        {
            if (string.IsNullOrWhiteSpace(entry.Worktime) || string.IsNullOrWhiteSpace(entry.Shift))
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Пропущена смена без времени работы");
                return;
            }

            try
            {
                var timeParts = entry.Worktime.Split('-');
                if (timeParts.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка парсинга времени");
                    return;
                }

                string startTimeString = timeParts[0].Trim();

                if (!TryParseTime(startTimeString, out TimeSpan startTime))
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Не удалось распарсить время: {startTimeString}");
                    return;
                }

                DateTime shiftStart = entry.Date.Date + startTime;

                if (shiftStart <= DateTime.Now.AddDays(-1))
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ⏭️ Смена в прошлом: {shiftStart}");
                    return;
                }

                DateTime notifyTime = ReminderHelper.GetNotificationTime(shiftStart, reminder);

                if (notifyTime <= DateTime.Now)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ⏭️ Время уведомления в прошлом: {notifyTime} (сейчас: {DateTime.Now})");
                    return;
                }

                string notificationMessage = entry.Shift.ToLower() switch
                {
                    string s when s.Contains("ноч") => $"{entry.Employees}, ночная смена начинается в {startTime:hh\\:mm}",
                    string s when s.Contains("днев") => $"{entry.Employees}, дневная смена начинается в {startTime:hh\\:mm}",
                    _ => $"{entry.Employees}, смена начинается в {startTime:hh\\:mm}"
                };

                System.Diagnostics.Debug.WriteLine($"[NotificationService] 📋 Планирование смены для {entry.Employees}:");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Дата смены: {shiftStart:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Время уведомления: {notifyTime:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Напоминание за: {reminder}");

                ScheduleNotification(
                    $"Смена: {entry.Shift}",
                    notificationMessage,
                    notifyTime,
                    SHIFT_CHANNEL_ID);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка планирования смены: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Stack: {ex.StackTrace}");
            }
        }

        private static bool TryParseTime(string timeString, out TimeSpan result)
        {
            result = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(timeString))
                return false;

            string[] formats = { "hh\\:mm", "h\\:mm", "hhmm", "hmm" };

            foreach (var format in formats)
            {
                if (TimeSpan.TryParseExact(timeString, format, null, out result))
                    return true;
            }

            return TimeSpan.TryParse(timeString, out result);
        }

        public static void ShowTestNotification()
        {
            ShowInstantNotification(
                "🧪 Тестовое уведомление",
                "Проверка работы уведомлений",
                CHAT_CHANNEL_ID);
        }

        public static void ScheduleTestNotification()
        {
            DateTime testTime = DateTime.Now.AddSeconds(10);

            System.Diagnostics.Debug.WriteLine($"[NotificationService] 🧪 Планирование тестового уведомления на {testTime:HH:mm:ss}");

            ScheduleNotification(
                "🧪 Тестовое уведомление",
                "Проверка планирования уведомлений",
                testTime,
                SHIFT_CHANNEL_ID);
        }

        /// <summary>
        /// Отменить все запланированные уведомления о сменах
        /// </summary>
        public static void CancelAllShiftNotifications()
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);

                if (alarmManager != null)
                {
                    // Отменяем все уведомления о сменах (по известным ID)
                    // Это сложно реализовать без сохранения списка ID
                    System.Diagnostics.Debug.WriteLine("[NotificationService] ⚠️ Отмена всех уведомлений требует сохранения ID");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка отмены: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Отменить уведомление о конкретной смене
        /// </summary>
        public static void CancelShiftNotification(ShiftEntry entry, ReminderOption reminder)
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService);

                if (alarmManager == null)
                    return;

                var timeParts = entry.Worktime.Split('-');
                if (timeParts.Length == 0)
                    return;

                string startTimeString = timeParts[0].Trim();

                if (!TryParseTime(startTimeString, out TimeSpan startTime))
                    return;

                DateTime shiftStart = entry.Date.Date + startTime;
                DateTime notifyTime = ReminderHelper.GetNotificationTime(shiftStart, reminder);
                
                int notificationId = ($"Смена: {entry.Shift}" + $"{entry.Employees}, смена начинается в {startTime:hh\\:mm}" + notifyTime.Ticks).GetHashCode() & 0x7FFFFFFF;

                var intent = new Intent(context, typeof(AlarmReceiver));
                intent.SetAction("android.intent.action.ALARM");

                var pendingIntent = PendingIntent.GetBroadcast(
                    context,
                    notificationId,
                    intent,
                    PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);

                if (pendingIntent != null)
                {
                    alarmManager.Cancel(pendingIntent);
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Отменено уведомление ID: {notificationId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка отмены: {ex.Message}");
            }
#endif
        }
    }
}