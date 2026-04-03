using System.Text.Json.Serialization;
using GrafikShared.Models;

namespace GrafikAdmin.Models;


/// Расписание на месяц

public class MonthlySchedule
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastModifiedAt")]
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("employees")]
    public List<string> Employees { get; set; } = [];

    [JsonPropertyName("secondLineEmployees")]
    public List<string> SecondLineEmployees { get; set; } = [];

    [JsonPropertyName("entries")]
    public List<ScheduleEntry> Entries { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    [JsonIgnore]
    public string FileName => $"schedule_{Year}_{Month:D2}.json";
}


/// Запись расписания для конкретного сотрудника на конкретный день

public class ScheduleEntry
{
    [JsonPropertyName("employeeName")]
    public string EmployeeName { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("shiftType")]
    public ShiftType ShiftType { get; set; } = ShiftType.DayOff;

    [JsonPropertyName("isSecondLine")]
    public bool IsSecondLine { get; set; } = false;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}


/// Тип смены

public enum ShiftType
{
    DayOff = 0,
    Day = 1,
    Night = 2,
    SecondLineDay = 3,
    Vacation = 4,
    SickLeave = 5,
    SecondLineNight = 6
}


/// Расширения для ShiftType

public static class ShiftTypeExtensions
{
    public static string ToDisplayString(this ShiftType shiftType) => shiftType switch
    {
        ShiftType.DayOff => "Выходной",
        ShiftType.Day => "Дневная",
        ShiftType.Night => "Ночная",
        ShiftType.SecondLineDay => "2 линия (день)",
        ShiftType.SecondLineNight => "2 линия (ночь)",
        ShiftType.Vacation => "Отпуск",
        ShiftType.SickLeave => "Больничный",
        _ => "—"
    };

    public static string ToShortString(this ShiftType shiftType) => shiftType switch
    {
        ShiftType.DayOff => "В",
        ShiftType.Day => "Д",
        ShiftType.Night => "Н",
        ShiftType.SecondLineDay => "2Д",
        ShiftType.SecondLineNight => "2Н",
        ShiftType.Vacation => "ОТП",
        ShiftType.SickLeave => "Б/Л",
        _ => ""
    };

    public static Color ToColor(this ShiftType shiftType) => shiftType switch
    {
        ShiftType.Day => Color.FromArgb("#FF8C00"),
        ShiftType.Night => Color.FromArgb("#00008B"),
        ShiftType.SecondLineDay => Color.FromArgb("#E65100"),
        ShiftType.SecondLineNight => Color.FromArgb("#1A237E"),
        ShiftType.Vacation => Color.FromArgb("#228B22"),
        ShiftType.SickLeave => Color.FromArgb("#DC143C"),
        ShiftType.DayOff => Colors.Black,
        _ => Colors.Black
    };


    // Является ли смена типом "2 линия" (сотрудник первой линии работает на второй)

    public static bool IsSecondLineShift(this ShiftType shiftType) =>
        shiftType == ShiftType.SecondLineDay || shiftType == ShiftType.SecondLineNight;
}