using System.Text.Json;

namespace GrafikAdmin.Services;


/// Сервис хранения постоянного списка сотрудников

public class EmployeeStorageService
{
    private const string EmployeesFileName = "employees_list.json";
    private readonly string _filePath;

    public EmployeeStorageService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, EmployeesFileName);
    }


    // Загрузить список сотрудников

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


    // Сохранить список сотрудников

    public async Task SaveAsync(EmployeeList employees)
    {
        employees.LastModifiedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(employees, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }


    // Добавить сотрудника

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


    // Удалить сотрудника

    public async Task<bool> RemoveEmployeeAsync(string name)
    {
        var list = await LoadAsync();

        bool removed = list.FirstLine.Remove(name) || list.SecondLine.Remove(name);

        if (removed)
            await SaveAsync(list);

        return removed;
    }


    // Переместить сотрудника между линиями

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


    // Получить всех сотрудников (обе линии)

    public async Task<List<string>> GetAllEmployeesAsync()
    {
        var list = await LoadAsync();
        return [.. list.FirstLine, .. list.SecondLine];
    }
}


/// Модель списка сотрудников

public class EmployeeList
{
    public List<string> FirstLine { get; set; } = [];
    public List<string> SecondLine { get; set; } = [];
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;


    // Все сотрудники (обе линии)

    public List<string> All => [.. FirstLine, .. SecondLine];


    // Общее количество сотрудников

    public int TotalCount => FirstLine.Count + SecondLine.Count;
}