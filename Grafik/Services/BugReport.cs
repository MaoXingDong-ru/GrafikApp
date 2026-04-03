using System;
using System.Text.Json.Serialization;

namespace Grafik.Services;


/// Тип обращения: баг-репорт или предложение

public enum BugReportType
{
    Bug,
    Feature
}


/// Статус обращения

public enum BugReportStatus
{
    Open,
    InProgress,
    Resolved,
    Rejected
}


/// Модель баг-репорта / предложения в Firebase

public class BugReport
{

    // Имя разработчика — только он видит панель управления

    public const string DeveloperName = "Сальников Степан";


    // Количество дней хранения завершённых репортов (resolved / rejected)

    public const int RetentionDays = 30;


    // Статусы, при которых репорт подлежит автоудалению по истечении срока

    public static readonly HashSet<string> CleanupStatuses = ["resolved", "rejected"];


    // Все доступные статусы для отображения в Picker

    public static readonly List<string> StatusOptions =
    [
        "🔴 Открыто",
        "🔧 В работе",
        "✔️ Решено",
        "⛔ Отклонено"
    ];


    // Маппинг отображаемого статуса → значение для Firebase

    public static string StatusDisplayToValue(int index) => index switch
    {
        1 => "inprogress",
        2 => "resolved",
        3 => "rejected",
        _ => "open"
    };


    // Маппинг значения Firebase → индекс в Picker

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


    // Ключ записи в Firebase. НЕ сериализуется при отправке.

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


    // Индекс текущего статуса для Picker

    [JsonIgnore]
    public int StatusIndex => StatusValueToIndex(Status);
}
