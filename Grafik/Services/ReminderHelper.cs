namespace Grafik.Services
{
    public static class ReminderHelper
    {
        public static System.TimeSpan ToTimeSpan(this ReminderOption reminder)
        {
            return reminder switch
            {
                ReminderOption.Minutes15 => System.TimeSpan.FromMinutes(15),
                ReminderOption.Hour1 => System.TimeSpan.FromHours(1),
                ReminderOption.Hours12 => System.TimeSpan.FromHours(12),
                _ => System.TimeSpan.Zero
            };
        }

        public static System.DateTime GetNotificationTime(System.DateTime shiftStart, ReminderOption reminder)
        {
            return shiftStart - reminder.ToTimeSpan();
        }
    }
}