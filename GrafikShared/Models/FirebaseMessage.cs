using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GrafikShared.Models;

/// <summary>
/// Сообщение в Firebase чате
/// </summary>
public class FirebaseMessage
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("fileData")]
    public string? FileData { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; } = false;

    [JsonPropertyName("readBy")]
    public Dictionary<string, bool>? ReadBy { get; set; }

    [JsonIgnore]
    public string FirebaseKey { get; set; } = string.Empty;
}
