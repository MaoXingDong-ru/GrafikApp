using System.Text.Json;

namespace GrafikAdmin.Services;

/// <summary>
/// Сервис хранения постоянного списка сотрудников
/// </summary>
public class EmployeeStorageService
{
    private const string EmployeesFileName = "employees_list.json";
    private readonly string _filePath;

    public EmployeeStorageService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, EmployeesFileName);
    }

    /// <summary>
    /// Загрузить список сотрудников
    /// </summary>
    public async Task<EmployeeList> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new EmployeeList();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<EmployeeList>(json) ?? new EmployeeList();
        }
        catch
        {
            return new EmployeeList();
        }
    }

    /// <summary>
    /// Сохранить список сотрудников
    /// </summary>
    public async Task SaveAsync(EmployeeList employees)
    {
        employees.LastModifiedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(employees, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }

    /// <summary>
    /// Добавить сотрудника
    /// </summary>
    public async Task<bool> AddEmployeeAsync(string name, bool isSecondLine)
    {
        var list = await LoadAsync();

        // Проверяем дубликаты
        if (list.FirstLine.Contains(name, StringComparer.OrdinalIgnoreCase) ||
            list.SecondLine.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (isSecondLine)
            list.SecondLine.Add(name);
        else
            list.FirstLine.Add(name);

        await SaveAsync(list);
        return true;
    }

    /// <summary>
    /// Удалить сотрудника
    /// </summary>
    public async Task<bool> RemoveEmployeeAsync(string name)
    {
        var list = await LoadAsync();

        bool removed = list.FirstLine.Remove(name) || list.SecondLine.Remove(name);

        if (removed)
            await SaveAsync(list);

        return removed;
    }

    /// <summary>
    /// Переместить сотрудника между линиями
    /// </summary>
    public async Task<bool> MoveEmployeeAsync(string name, bool toSecondLine)
    {
        var list = await LoadAsync();

        if (toSecondLine)
        {
            if (list.FirstLine.Remove(name))
            {
                list.SecondLine.Add(name);
                await SaveAsync(list);
                return true;
            }
        }
        else
        {
            if (list.SecondLine.Remove(name))
            {
                list.FirstLine.Add(name);
                await SaveAsync(list);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Получить всех сотрудников (обе линии)
    /// </summary>
    public async Task<List<string>> GetAllEmployeesAsync()
    {
        var list = await LoadAsync();
        return [.. list.FirstLine, .. list.SecondLine];
    }
}

/// <summary>
/// Модель списка сотрудников
/// </summary>
public class EmployeeList
{
    public List<string> FirstLine { get; set; } = [];
    public List<string> SecondLine { get; set; } = [];
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Все сотрудники (обе линии)
    /// </summary>
    public List<string> All => [.. FirstLine, .. SecondLine];

    /// <summary>
    /// Общее количество сотрудников
    /// </summary>
    public int TotalCount => FirstLine.Count + SecondLine.Count;
}