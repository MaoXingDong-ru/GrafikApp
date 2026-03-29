using System;
using System.Text.Json.Serialization;

namespace Grafik.Services;

/// <summary>
/// Статус запроса на обмен сменами
/// </summary>
public enum SwapRequestStatus
{
    Pending,    // Ожидает одобрения
    Approved,   // Одобрено админом
    Denied      // Отклонено админом
}

/// <summary>
/// Запрос на обмен сменами между сотрудниками
/// </summary>
