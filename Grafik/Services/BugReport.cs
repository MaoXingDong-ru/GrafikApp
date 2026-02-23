using System;
using System.Text.Json.Serialization;

namespace Grafik.Services;

/// <summary>
/// Тип обращения: баг-репорт или предложение
/// </summary>
public enum BugReportType
{
    Bug,
    Feature
}

/// <summary>
/// Статус обращения
/// </summary>
public enum BugReportStatus
{
    Open,
    InProgress,
    Resolved,
    Rejected
}

/// <summary>
/// Модель баг-репорта / предложения в Firebase
/// </summary>
public class BugReport
{
    /// <summary>
    /// Имя разработчика — только он видит панель управления
    /// </summary>
    public const string DeveloperName = "Сальников Степан";

    /// <summary>
    /// Количество дней хранения завершённых репортов (resolved / rejected)
    /// </summary>
    public const int RetentionDays = 30;

    /// <summary>
    /// Статусы, при которых репорт подлежит автоудалению по истечении срока
    /// </summary>
    public static readonly HashSet<string> CleanupStatuses = ["resolved", "rejected"];

    /// <summary>
    /// Все доступные статусы для отображения в Picker
    /// </summary>
    public static readonly List<string> StatusOptions =
    [
        "🔴 Открыто",
        "🔧 В работе",
        "✔️ Решено",
        "⛔ Отклонено"
    ];

    /// <summary>
    /// Маппинг отображаемого статуса → значение для Firebase
    /// </summary>
    public static string StatusDisplayToValue(int index) => index switch
    {
        1 => "inprogress",
        2 => "resolved",
        3 => "rejected",
        _ => "open"
    };

    /// <summary>
    /// Маппинг значения Firebase → индекс в Picker
    /// </summary>
    public static int StatusValueToIndex(string status) => status switch
    {
        "inprogress" => 1,
        "resolved" => 2,
        "rejected" => 3,
        _ => 0
    };

    public static bool IsDeveloper(string userName) =>
        string.Equals(userName, DeveloperName, StringComparison.OrdinalIgnoreCase);

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "bug";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public string Steps { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("devComment")]
    public string DevComment { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Ключ записи в Firebase. НЕ сериализуется при отправке.
    /// </summary>
    [JsonIgnore]
    public string FirebaseKey { get; set; } = string.Empty;

    // --- Вычисляемые свойства для UI ---

    [JsonIgnore]
    public string TypeDisplay => Type switch
    {
        "feature" => "💡 Предложение",
        _ => "🐛 Баг-репорт"
    };

    [JsonIgnore]
    public string StatusDisplay => Status switch
    {
        "inprogress" => "🔧 В работе",
        "resolved" => "✔️ Решено",
        "rejected" => "⛔ Отклонено",
        _ => "🔴 Открыто"
    };

    [JsonIgnore]
    public Color StatusColor => Status switch
    {
        "inprogress" => Color.FromArgb("#1976D2"), // синий
        "resolved" => Color.FromArgb("#7B1FA2"),   // фиолетовый
        "rejected" => Color.FromArgb("#9E9E9E"),   // серый
        _ => Color.FromArgb("#F44336")             // красный
    };

    [JsonIgnore]
    public Color CardBackgroundColor => Status switch
    {
        "inprogress" => Color.FromArgb("#E3F2FD"),
        "resolved" => Color.FromArgb("#F3E5F5"),
        "rejected" => Color.FromArgb("#F5F5F5"),
        _ => Color.FromArgb("#FFEBEE")
    };

    [JsonIgnore]
    public bool HasDevComment => !string.IsNullOrWhiteSpace(DevComment);

    [JsonIgnore]
    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Индекс текущего статуса для Picker
    /// </summary>
    [JsonIgnore]
    public int StatusIndex => StatusValueToIndex(Status);
}
