using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Android.OS;
using Android.Content.PM;
using AndroidX.Core.Content;
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

        /// <summary>
        /// Ключ в Preferences для хранения списка запланированных notification ID
        /// </summary>
        private const string SCHEDULED_IDS_KEY = "ScheduledNotificationIds";

        /// <summary>
        /// Ключ для хранения имени сотрудника, для которого запланированы уведомления
        /// </summary>
        private const string SCHEDULED_EMPLOYEE_KEY = "ScheduledNotificationEmployee";

#if ANDROID
        /// <summary>
        /// Проверяет, разрешены ли уведомления на устройстве
        /// </summary>
        public static bool AreNotificationsEnabled()
        {
            var context = Android.App.Application.Context;
            var notificationManager = NotificationManagerCompat.From(context);
            return notificationManager.AreNotificationsEnabled();
        }

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

        #region Хранение запланированных ID

        /// <summary>
        /// Получить список всех сохранённых notification ID
        /// </summary>
        private static List<int> GetScheduledIds()
        {
            try
            {
                var json = Preferences.Get(SCHEDULED_IDS_KEY, "[]");
                return JsonSerializer.Deserialize<List<int>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Сохранить список notification ID
        /// </summary>
        private static void SaveScheduledIds(List<int> ids)
        {
            var json = JsonSerializer.Serialize(ids);
            Preferences.Set(SCHEDULED_IDS_KEY, json);
        }

        /// <summary>
        /// Добавить ID в список запланированных
        /// </summary>
        private static void TrackNotificationId(int notificationId)
        {
            var ids = GetScheduledIds();
            if (!ids.Contains(notificationId))
            {
                ids.Add(notificationId);
                SaveScheduledIds(ids);
            }
        }

        /// <summary>
        /// Очистить список запланированных ID
        /// </summary>
        private static void ClearTrackedIds()
        {
            SaveScheduledIds([]);
        }

        #endregion

        /// <summary>
        /// Генерация стабильного ID на основе данных смены (не зависит от момента вызова)
        /// </summary>
        private static int GenerateStableNotificationId(string title, string message, DateTime notifyTime)
        {
            // Округляем время до минуты для стабильности
            var roundedTime = new DateTime(notifyTime.Year, notifyTime.Month, notifyTime.Day,
                notifyTime.Hour, notifyTime.Minute, 0);
            return (title + message + roundedTime.Ticks).GetHashCode() & 0x7FFFFFFF;
        }

        /// <summary>
        /// Показать немедленное уведомление (для новых сообщений в чате)
        /// </summary>
        public static void ShowInstantNotification(string title, string message, string channelId = CHAT_CHANNEL_ID)
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;

                // ✅ Проверяем разрешение POST_NOTIFICATIONS (Android 13+)
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    var permissionStatus = ContextCompat.CheckSelfPermission(context, 
                        Android.Manifest.Permission.PostNotifications);

                    if (permissionStatus != Permission.Granted)
                    {
                        System.Diagnostics.Debug.WriteLine("[NotificationService] ❌ POST_NOTIFICATIONS не предоставлен! Уведомление не будет показано.");
                        return;
                    }
                }

                // ✅ Проверяем, включены ли уведомления в настройках системы
                var notificationManager = NotificationManagerCompat.From(context);
                if (!notificationManager.AreNotificationsEnabled())
                {
                    System.Diagnostics.Debug.WriteLine("[NotificationService] ❌ Уведомления отключены в настройках устройства!");
                    return;
                }

                // ✅ Убеждаемся что каналы созданы
                CreateNotificationChannels();

                int notificationId = (title + message + DateTime.Now.Ticks).GetHashCode() & 0x7FFFFFFF;

                // ✅ Создаём Intent для открытия приложения при нажатии на уведомление
                var activityIntent = new Intent(context, typeof(MainActivity));
                activityIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                var pendingIntent = PendingIntent.GetActivity(
                    context,
                    notificationId,
                    activityIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                var notificationBuilder = new NotificationCompat.Builder(context, channelId)
                    .SetContentTitle(title)
                    .SetContentText(message)
                    .SetAutoCancel(true)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetPriority(NotificationCompat.PriorityHigh)
                    .SetDefaults((int)NotificationDefaults.All)
                    .SetContentIntent(pendingIntent)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(message));

                notificationManager.Notify(notificationId, notificationBuilder.Build());

                System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Системное уведомление отправлено: {title} (ID: {notificationId})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Stack: {ex.StackTrace}");
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
                
                int notificationId = GenerateStableNotificationId(title, message, notifyTime);

                // Абсолютное время в миллисекундах (UTC -> epoch)
                long triggerAtMillis = new DateTimeOffset(notifyTime).ToUnixTimeMilliseconds();

                System.Diagnostics.Debug.WriteLine($"[NotificationService] 📅 Планирование уведомления:");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   ID: {notificationId}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Название: {title}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Сообщение: {message}");
                System.Diagnostics.Debug.WriteLine($"[NotificationService]   Время: {notifyTime:yyyy-MM-dd HH:mm:ss}");
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

                        // Сохраняем ID для последующей отмены
                        TrackNotificationId(notificationId);

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

                if (alarmManager == null)
                    return;

                var ids = GetScheduledIds();
                System.Diagnostics.Debug.WriteLine($"[NotificationService] 🗑️ Отмена {ids.Count} запланированных уведомлений...");

                foreach (var id in ids)
                {
                    try
                    {
                        var intent = new Intent(context, typeof(AlarmReceiver));
                        intent.SetAction("android.intent.action.ALARM");

                        var pendingIntent = PendingIntent.GetBroadcast(
                            context,
                            id,
                            intent,
                            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);

                        if (pendingIntent != null)
                        {
                            alarmManager.Cancel(pendingIntent);
                            pendingIntent.Cancel();
                            System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Отменён будильник ID: {id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NotificationService] ⚠️ Ошибка отмены ID {id}: {ex.Message}");
                    }
                }

                ClearTrackedIds();
                Preferences.Remove(SCHEDULED_EMPLOYEE_KEY);

                System.Diagnostics.Debug.WriteLine("[NotificationService] ✅ Все уведомления отменены");
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

                string notificationMessage = entry.Shift.ToLower() switch
                {
                    string s when s.Contains("ноч") => $"{entry.Employees}, ночная смена начинается в {startTime:hh\\:mm}",
                    string s when s.Contains("днев") => $"{entry.Employees}, дневная смена начинается в {startTime:hh\\:mm}",
                    _ => $"{entry.Employees}, смена начинается в {startTime:hh\\:mm}"
                };

                int notificationId = GenerateStableNotificationId(
                    $"Смена: {entry.Shift}",
                    notificationMessage,
                    notifyTime);

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
                    pendingIntent.Cancel();
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Отменено уведомление ID: {notificationId}");
                }

                // Удаляем из списка
                var ids = GetScheduledIds();
                ids.Remove(notificationId);
                SaveScheduledIds(ids);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] ❌ Ошибка отмены: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Перепланировать все уведомления для конкретного сотрудника.
        /// Сначала отменяет все старые, затем планирует новые.
        /// </summary>
        public static void RescheduleAllForEmployee(string employeeName, List<ShiftEntry> schedule, ReminderOption reminder)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationService] 🔄 Перепланирование уведомлений для {employeeName}");

            // 1. Отменяем ВСЕ старые будильники
            CancelAllShiftNotifications();

            // 2. Сохраняем имя сотрудника, для которого планируем
            Preferences.Set(SCHEDULED_EMPLOYEE_KEY, employeeName);

            // 3. Планируем только для текущего сотрудника
            var employeeEntries = schedule
                .Where(e => string.Equals(e.Employees, employeeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int count = 0;
            foreach (var entry in employeeEntries)
            {
                ScheduleShiftNotification(entry, reminder);
                count++;
            }

            System.Diagnostics.Debug.WriteLine($"[NotificationService] ✅ Перепланировано {count} уведомлений для {employeeName}");
        }

        /// <summary>
        /// Получить имя сотрудника, для которого запланированы текущие уведомления
        /// </summary>
        public static string GetScheduledEmployee()
        {
            return Preferences.Get(SCHEDULED_EMPLOYEE_KEY, string.Empty);
        }
    }
}