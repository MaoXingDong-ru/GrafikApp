using System;
using System.Text.Json.Serialization;

namespace GrafikShared.Services;

/// <summary>
/// Запрос на обмен сменами между сотрудниками
/// </summary>
public class ShiftSwapRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("requesterName")]
    public string RequesterName { get; set; } = string.Empty;

    [JsonPropertyName("requesterDate")]
    public DateTime RequesterDate { get; set; }

    [JsonPropertyName("requesterShift")]
    public string RequesterShift { get; set; } = string.Empty;

    [JsonPropertyName("requesterIsSecondLine")]
    public bool RequesterIsSecondLine { get; set; }

    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;

    [JsonPropertyName("targetDate")]
    public DateTime TargetDate { get; set; }

    [JsonPropertyName("targetShift")]
    public string TargetShift { get; set; } = string.Empty;

    [JsonPropertyName("targetIsSecondLine")]
    public bool TargetIsSecondLine { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("adminComment")]
    public string AdminComment { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("processedAt")]
    public DateTime? ProcessedAt { get; set; }

    [JsonIgnore]
    public string FirebaseKey { get; set; } = string.Empty;

    // --- UI свойства ---

    [JsonIgnore]
    public string StatusDisplay => Status switch
    {
        "approved" => "✅ Одобрено",
        "denied" => "❌ Отклонено",
        _ => "⏳ Ожидает"
    };

    [JsonIgnore]
    public string StatusColor => Status switch
    {
        "approved" => "#4CAF50",
        "denied" => "#F44336",
        _ => "#FF9800"
    };

    [JsonIgnore]
    public string CardBackgroundColor => Status switch
    {
        "approved" => "#1B5E20",
        "denied" => "#B71C1C",
        _ => "#E65100"
    };

    [JsonIgnore]
    public string RequesterDateDisplay => RequesterDate.ToString("dd.MM.yyyy (ddd)");

    [JsonIgnore]
    public string TargetDateDisplay => TargetDate.ToString("dd.MM.yyyy (ddd)");

    [JsonIgnore]
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string LineDisplay => RequesterIsSecondLine ? "2-я линия" : "1-я линия";

    [JsonIgnore]
    public string SwapDescription =>
        $"{RequesterName}: {RequesterDateDisplay} ({RequesterShift})\n↔️\n{TargetName}: {TargetDateDisplay} ({TargetShift})";

    [JsonIgnore]
    public bool IsPending => Status == "pending";

    [JsonIgnore]
    public bool IsProcessed => Status == "approved" || Status == "denied";
}