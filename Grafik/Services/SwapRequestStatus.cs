using System;
using System.Text.Json.Serialization;

namespace Grafik.Services;


/// Статус запроса на обмен сменами

public enum SwapRequestStatus
{
    Pending,    // Ожидает одобрения
    Approved,   // Одобрено админом
    Denied      // Отклонено админом
}


/// Запрос на обмен сменами между сотрудниками

