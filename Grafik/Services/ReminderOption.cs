namespace Grafik.Services
{
    // Варианты напоминания перед сменой
    public enum ReminderOption
    {
        None = 0, // Без уведомлений
        FiveMinutesBefore = 5,
        FifteenMinutesBefore = 15,
        ThirtyMinutesBefore = 30,
        OneHourBefore = 60,
        TwoHoursBefore = 120,
        OneDayBefore = 1440
    }
}