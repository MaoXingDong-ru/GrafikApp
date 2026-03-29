using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GrafikShared.Models;

/// <summary>
/// Расписание на месяц для синхронизации через Firebase
/// </summary>
public class SharedSchedule
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
    public List<string> Employees { get; set; } = new();

    [JsonPropertyName("secondLineEmployees")]
    public List<string> SecondLineEmployees { get; set; } = new();

    [JsonPropertyName("entries")]
    public List<SharedScheduleEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public string FileName => $"{Month:D2}_{Year}";

    [JsonIgnore]
    public string DisplayName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
}

/// <summary>
/// Запись расписания
/// </summary>
public class SharedScheduleEntry
{
    [JsonPropertyName("employeeName")]
    public string EmployeeName { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("shiftType")]
    public int ShiftType { get; set; }

    [JsonPropertyName("isSecondLine")]
    public bool IsSecondLine { get; set; }
}
