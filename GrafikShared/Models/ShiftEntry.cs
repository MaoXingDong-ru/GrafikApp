using System.Text.Json.Serialization;

namespace GrafikShared.Models;

/// <summary>
/// Запись расписания смены
/// </summary>
public class ShiftEntry
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("shift")]
    public string Shift { get; set; } = string.Empty;

    [JsonPropertyName("worktime")]
    public string Worktime { get; set; } = string.Empty;

    [JsonPropertyName("employees")]
    public string Employees { get; set; } = string.Empty;

    [JsonPropertyName("isSecondLine")]
    public bool IsSecondLine { get; set; } = false;

    // UI-only свойства (не сериализуются в JSON)
    [JsonIgnore]
    public List<string> OtherEmployeesWithSameShift { get; set; } = [];

    [JsonIgnore]
    public string? DisplayOtherEmployees { get; set; }

    [JsonIgnore]
    public string? SecondLinePartner { get; set; }
}