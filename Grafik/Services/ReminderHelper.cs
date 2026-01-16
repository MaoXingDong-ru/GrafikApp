namespace Grafik.Services
{
    public static class ReminderHelper
    {
        public static DateTime GetNotificationTime(DateTime shiftStart, ReminderOption reminder)
        {
            return reminder switch
            {
                ReminderOption.FiveMinutesBefore => shiftStart.AddMinutes(-5),
                ReminderOption.FifteenMinutesBefore => shiftStart.AddMinutes(-15),
                ReminderOption.ThirtyMinutesBefore => shiftStart.AddMinutes(-30),
                ReminderOption.OneHourBefore => shiftStart.AddHours(-1),
                ReminderOption.TwoHoursBefore => shiftStart.AddHours(-2),
                ReminderOption.OneDayBefore => shiftStart.AddDays(-1),
                _ => shiftStart.AddMinutes(-30) // По умолчанию 30 минут
            };
        }

        /// <summary>
        /// Получить отображаемое имя опции напоминания
        /// </summary>
        public static string GetDisplayName(ReminderOption reminder)
        {
            return reminder switch
            {
                ReminderOption.FiveMinutesBefore => "За 5 минут",
                ReminderOption.FifteenMinutesBefore => "За 15 минут",
                ReminderOption.ThirtyMinutesBefore => "За 30 минут",
                ReminderOption.OneHourBefore => "За 1 час",
                ReminderOption.TwoHoursBefore => "За 2 часа",
                ReminderOption.OneDayBefore => "За 1 день",
                _ => "За 30 минут"
            };
        }
    }
}