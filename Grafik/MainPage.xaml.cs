using Microsoft.Maui.Storage;
using ClosedXML.Excel;
using System.Text.Json;

namespace Grafik;

public partial class MainPage : ContentPage
{
    private string scheduleFilePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");
    private string employeeListFilePath = Path.Combine(FileSystem.AppDataDirectory, "employees.json");


    public MainPage()
    {
        InitializeComponent();
        LoadEmployeeList();

        string selectedEmployee = Preferences.Get("SelectedEmployee", string.Empty);

        if (!string.IsNullOrEmpty(selectedEmployee))
        {
            // Если сотрудник был выбран ранее, автоматически загружаем его расписание
             Navigation.PushAsync(new EmployeeSchedulePage(selectedEmployee));
        }
    }
    

    // Загружаем список сотрудников
    private async void OnLoadExcelClicked(object sender, EventArgs e)
    {
        var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }, // .xlsx MIME type
            { DevicePlatform.WinUI, new[] { ".xlsx" } }
        });

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Выберите файл с расписанием",
            FileTypes = customFileType
        });

        if (result != null)
        {
            var fileBytes = File.ReadAllBytes(result.FullPath);

            // Извлекаем расписание
            var schedule = ExtractScheduleFromExcel(fileBytes);

            // Сохраняем расписание в JSON
            SaveScheduleToJson(schedule);

            // Извлекаем уникальные имена сотрудников
            var uniqueEmployees = schedule.Select(s => s.Employees).Distinct().ToList();

            // Сохраняем расписание в JSON
            SaveScheduleToJson(schedule);

            // Сохраняем уникальные имена сотрудников в отдельный JSON
            SaveEmployeesToJson(uniqueEmployees);

            // Заполняем Picker списком сотрудников
            if (uniqueEmployees.Count > 0)
            {
                EmployeePicker.ItemsSource = uniqueEmployees;
            }
            else
            {
                DisplayMessage("Не удалось извлечь сотрудников.");
            }
        }
        else
        {
            DisplayMessage("Файл не выбран.");
        }
      

    }

    // Извлечение расписания из Excel
    public static List<ShiftEntry> ExtractScheduleFromExcel(byte[] fileData)
    {
        var result = new List<ShiftEntry>();

        using var stream = new MemoryStream(fileData);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);  // Первый лист

        for (int day = 1; day <= 30; day++)
        {
            int dayColumn = day * 2;  // Колонки для дневной и ночной смены

            for (int row = 4; row <= 20; row++)  // Строки с 4 по 20
            {
                var name = worksheet.Cell(row, 1).GetString();
                var dayShift = worksheet.Cell(row, dayColumn).GetString();
                var nightShift = worksheet.Cell(row, dayColumn + 1).GetString();

                if (!string.IsNullOrWhiteSpace(dayShift) || !string.IsNullOrWhiteSpace(nightShift))
                {
                    var shiftText = "";
                    if (!string.IsNullOrWhiteSpace(dayShift))
                        shiftText += "Дневная ";
                    if (!string.IsNullOrWhiteSpace(nightShift))
                        shiftText += "Ночная";

                    var entry = result.FirstOrDefault(e => e.Employees == name && e.Date.Day == day);
                    if (entry == null)
                    {
                        result.Add(new ShiftEntry
                        {
                            Employees = name,
                            Date = new DateTime(DateTime.Now.Year, DateTime.Now.Month, day),
                            Shift = shiftText.Trim(),
                            OtherEmployeesWithSameShift = new List<string> { name }
                        });
                    }
                    else
                    {
                        entry.Shift += ", " + shiftText.Trim();
                        if (!entry.OtherEmployeesWithSameShift.Contains(name))
                        {
                            entry.OtherEmployeesWithSameShift.Add(name);
                        }
                    }
                }
            }
        }

        return result;
    }

    // Сохранение Сотрудников в JSON
    private void SaveEmployeesToJson(List<string> employees)
    {
        var employeeJson = JsonSerializer.Serialize(employees, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(employeeListFilePath, employeeJson);
    }

    // Сохранение расписания в JSON
    private void SaveScheduleToJson(List<ShiftEntry> schedule)
    {

        var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");
        File.WriteAllText(filePath, json);

    }

    // Загрузка списка сотрудников
    private void LoadEmployeeList()
    {
        // Проверка существования JSON файла
        if (File.Exists(employeeListFilePath))
        {
            // Если файл существует, загружаем данные сотрудников из JSON
            string jsonData = File.ReadAllText(employeeListFilePath);
            var employees = JsonSerializer.Deserialize<List<string>>(jsonData);

            // Заполняем Picker списком сотрудников
            if (employees != null && employees.Count > 0)
            {
                EmployeePicker.IsVisible = true;
                Instruction2.IsVisible = true;
                EmployeePicker.ItemsSource = employees;
            }
        }
        else
        {
            EmployeePicker.IsVisible = false; // Скрываем Picker, так как данных нет
        }
    }


    private void DisplayMessage(string message)
    {
        // Отображение сообщения (например, через Label)
        MessageLabel.IsVisible = true;
        MessageLabel.Text = message;
    }

    // Обработка выбора сотрудника
    internal async void OnEmployeeSelected(object sender, EventArgs e)
    {
        var selectedEmployee = EmployeePicker.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedEmployee))
        {
            // Сохраняем выбранного сотрудника в настройках
            Preferences.Set("SelectedEmployee", selectedEmployee);
            LoadSchedule.IsVisible = true;
        }

    }
    internal async void LoadScheduleForEmployeer(object sender, EventArgs e)
    {
        var selectedEmployee = EmployeePicker.SelectedItem as string;

        if (!string.IsNullOrEmpty(selectedEmployee))
        {
            // Сохраняем выбранного сотрудника в Preferences, чтобы он сохранялся после перезапуска
            Preferences.Set("SelectedEmployee", selectedEmployee);

            // Переходим на страницу расписания для выбранного сотрудника
            await Navigation.PushAsync(new EmployeeSchedulePage(selectedEmployee));
        }
        else
        {
            await DisplayAlert("Ошибка", "Пожалуйста, выберите сотрудника", "OK");
        }
    }

}
