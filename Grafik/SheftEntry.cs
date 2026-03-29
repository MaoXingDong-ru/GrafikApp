using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Maui.Graphics;

namespace Grafik
{
    public class ShiftEntry
    {
        // Основная информация
        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        [JsonPropertyName("shift")]
        public string Shift { get; set; } = string.Empty;

        [JsonPropertyName("worktime")]
        public string Worktime { get; set; } = string.Empty;

        // Сотрудники
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

        // Для календаря
        [JsonIgnore]
        public Color TileColor { get; set; } = Colors.Transparent;

        [JsonIgnore]
        public Color BorderColor { get; set; } = Colors.Transparent;

        [JsonIgnore]
        public bool IsVisibleDay { get; set; } = true;

        [JsonIgnore]
        public string ShiftDisplayText { get; set; } = string.Empty;
    }
}