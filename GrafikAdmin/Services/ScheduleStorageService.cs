using System.Text.Json;
using GrafikAdmin.Models;

namespace GrafikAdmin.Services;

/// <summary>
/// Информация о расписании для списка
/// </summary>
public record ScheduleInfo(int Year, int Month, string DisplayName, DateTime CreatedAt);

/// <summary>
/// Сервис хранения расписаний (максимум 3 месяца)
/// </summary>
public class ScheduleStorageService
{
    private const int MaxStoredMonths = 3;
    private readonly string _storageDirectory;

    public ScheduleStorageService()
    {
        _storageDirectory = Path.Combine(FileSystem.AppDataDirectory, "schedules");
        
        if (!Directory.Exists(_storageDirectory))
            Directory.CreateDirectory(_storageDirectory);
    }

    public async Task SaveScheduleAsync(MonthlySchedule schedule)
    {
        schedule.LastModifiedAt = DateTime.UtcNow;
        
        var filePath = GetFilePath(schedule.Year, schedule.Month);
        var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
        
        await File.WriteAllTextAsync(filePath, json);
        
        System.Diagnostics.Debug.WriteLine($"[ScheduleStorage] Сохранено: {schedule.DisplayName}");
        
        CleanupOldSchedules();
    }

    public async Task<MonthlySchedule?> LoadScheduleAsync(int year, int month)
    {
        var filePath = GetFilePath(year, month);
        
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<MonthlySchedule>(json);
    }

    public List<ScheduleInfo> GetAvailableSchedules()
    {
        var result = new List<ScheduleInfo>();
        
        if (!Directory.Exists(_storageDirectory))
            return result;

        foreach (var file in Directory.GetFiles(_storageDirectory, "schedule_*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var parts = fileName.Split('_');
            
            if (parts.Length == 3 
                && int.TryParse(parts[1], out int year) 
                && int.TryParse(parts[2], out int month))
            {
                var displayName = new DateTime(year, month, 1).ToString("MMMM yyyy");
                var createdAt = File.GetCreationTime(file);
                result.Add(new ScheduleInfo(year, month, displayName, createdAt));
            }
        }

        return result.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList();
    }

    public bool ScheduleExists(int year, int month) => File.Exists(GetFilePath(year, month));

    public void DeleteSchedule(int year, int month)
    {
        var filePath = GetFilePath(year, month);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private void CleanupOldSchedules()
    {
        var schedules = GetAvailableSchedules();
        
        foreach (var schedule in schedules.Skip(MaxStoredMonths))
        {
            DeleteSchedule(schedule.Year, schedule.Month);
            System.Diagnostics.Debug.WriteLine($"[ScheduleStorage] Автоудаление: {schedule.DisplayName}");
        }
    }

    private string GetFilePath(int year, int month) 
        => Path.Combine(_storageDirectory, $"schedule_{year}_{month:D2}.json");
}