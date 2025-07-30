using ClosedXML.Excel;
using Microsoft.Maui.Storage;
using System.Globalization;
using System.Text.Json;

namespace Grafik;

public partial class MainPage : ContentPage
{
    private readonly string scheduleFilePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");
    private readonly string employeeListFilePath = Path.Combine(FileSystem.AppDataDirectory, "employees.json");

    public MainPage()
    {
        InitializeComponent();
        SetupInitialState();
        LoadEmployeeList();
    }

    private void SetupInitialState()
    {
        DeleteData.IsVisible = false;
        DeleteData.TranslationX = -120;

        string selectedEmployee = Preferences.Get("SelectedEmployee", string.Empty);
        if (!string.IsNullOrEmpty(selectedEmployee))
        {
            Navigation.PushAsync(new EmployeeSchedulePage(selectedEmployee));
        }
    }

    private AppSettings LoadSettings()
    {
        string settingsPath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");
        if (File.Exists(settingsPath))
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        return new AppSettings();
    }

    private async void OnLoadExcelClicked(object sender, EventArgs e)
    {
        var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
            { DevicePlatform.WinUI, new[] { ".xlsx", ".xls" } }
        });

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите файл с расписанием",
                FileTypes = customFileType
            });

            if (result != null)
            {
                await ProcessExcelFile(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            DisplayMessage($"Ошибка: {ex.Message}");
        }
    }

    private async Task ProcessExcelFile(string filePath)
    {
        var fileBytes = File.ReadAllBytes(filePath);
        var settings = LoadSettings();

        if (settings?.EmployeeCount <= 0)
        {
            DisplayMessage("Настройки не заданы. Откройте страницу настроек.");
            return;
        }

        var schedule = ExtractScheduleFromExcel(fileBytes, settings);
        SaveScheduleToJson(schedule);

        var uniqueEmployees = schedule.Select(s => s.Employees).Distinct().ToList();
        SaveEmployeesToJson(uniqueEmployees);

        if (uniqueEmployees.Count > 0)
        {
            EmployeePicker.ItemsSource = uniqueEmployees;
            await AnimateToStep2UI();
            await ShowDeleteButton();
            DisplayMessage("Данные успешно загружены");
        }
        else
        {
            DisplayMessage("Не удалось извлечь сотрудников.");
        }
    }

    private async Task AnimateToStep2UI()
    {
        await FirstStepGroup.FadeTo(0, 300);
        FirstStepGroup.IsVisible = false;

        SecondStepGroup.Opacity = 0;
        SecondStepGroup.IsVisible = true;

        await SecondStepGroup.FadeTo(1, 300);
    }

    private async Task AnimateBackToStep1UI()
    {
        await SecondStepGroup.FadeTo(0, 300);
        SecondStepGroup.IsVisible = false;

        FirstStepGroup.Opacity = 0;
        FirstStepGroup.IsVisible = true;

        await FirstStepGroup.FadeTo(1, 300);
    }

    private async Task ShowDeleteButton()
    {
        DeleteData.IsVisible = true;
        await DeleteData.TranslateTo(0, 0, 300, Easing.SinOut);
    }

    private async Task HideDeleteButton()
    {
        await DeleteData.TranslateTo(-50, 0, 200, Easing.SinIn);
        DeleteData.IsVisible = false;
    }

    public static List<ShiftEntry> ExtractScheduleFromExcel(byte[] fileData, AppSettings settings)
    {
        var result = new List<ShiftEntry>();
        using var stream = new MemoryStream(fileData);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var invalidNames = new HashSet<string> { "дата", "время", "date", "time", "", null };
        var dayColumns = GetValidDayColumns(worksheet);

        int startRowFirstLine = 4;
        int firstLineEndRow = ProcessEmployeeGroup(worksheet, startRowFirstLine, settings.EmployeeCount,
                                                 result, invalidNames, false, dayColumns);

        if (settings.HasSecondLineEmployees || settings.SecondLineEmployeeCount > 0)
        {
            int startRowSecondLine = firstLineEndRow + 4;
            ProcessEmployeeGroup(worksheet, startRowSecondLine, settings.SecondLineEmployeeCount,
                               result, invalidNames, true, dayColumns);
        }

        return result;
    }

    private static List<(int ColumnIndex, DateTime Date)> GetValidDayColumns(IXLWorksheet worksheet)
    {
        var result = new List<(int, DateTime)>();
        int col = 2;

        while (true)
        {
            var cell = worksheet.Cell(1, col);
            if (DateTime.TryParse(cell.GetString(), out DateTime parsedDate))
            {
                result.Add((col, parsedDate));
                col += 2;
            }
            else break;
        }

        return result;
    }

    private static int ProcessEmployeeGroup(IXLWorksheet worksheet, int startRow, int count,
                                          List<ShiftEntry> result, HashSet<string> invalidNames,
                                          bool isSecondLine, List<(int ColumnIndex, DateTime Date)> dayColumns)
    {
        int endRow = startRow + count;
        int lastProcessedRow = startRow;
        var ExclusionWords = new[] { "отпуск", "замещает", "Игорь", "Ангелина" };

        for (int row = startRow; row < endRow; row++)
        {
            var nameCell = worksheet.Cell(row, 1);
            string rawName = nameCell.GetString().Trim().ToLower();

            if (invalidNames.Contains(rawName)) continue;

            string name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(rawName);

            foreach (var (col, date) in dayColumns)
            {
                var dayShift = worksheet.Cell(row, col).GetString().Trim();
                var nightShift = worksheet.Cell(row, col + 1).GetString().Trim();

                bool shouldExclude = ExclusionWords.Any(keyword =>
                dayShift.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                nightShift.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!shouldExclude && (!string.IsNullOrWhiteSpace(dayShift) || !string.IsNullOrWhiteSpace(nightShift)))
                {
                    result.Add(new ShiftEntry
                    {
                        Employees = name,
                        Date = date,
                        Shift = $"{(string.IsNullOrEmpty(dayShift) ? "" : "Дневная")} {(string.IsNullOrEmpty(nightShift) ? "" : "Ночная")}".Trim(),
                        Worktime = $"{(string.IsNullOrEmpty(dayShift) ? "" : "09:00-21:00")} {(string.IsNullOrEmpty(nightShift) ? "" : "21:00-09:00")}".Trim(),
                        IsSecondLine = isSecondLine
                    });
                }
            }

            lastProcessedRow = row;
        }

        return lastProcessedRow;
    }

    private void SaveEmployeesToJson(List<string> employees)
    {
        var json = JsonSerializer.Serialize(employees, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(employeeListFilePath, json);
    }

    private static void SaveScheduleToJson(List<ShiftEntry> schedule)
    {
        var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "schedule.json"), json);
    }

    private void LoadEmployeeList()
    {
        if (File.Exists(employeeListFilePath))
        {
            var json = File.ReadAllText(employeeListFilePath);
            var employees = JsonSerializer.Deserialize<List<string>>(json);

            if (employees?.Count > 0)
            {
                EmployeePicker.ItemsSource = employees;
                _ = AnimateToStep2UI();
                _ = ShowDeleteButton();
            }
        }
    }

    private void DisplayMessage(string message)
    {
        MessageLabel.Text = message;
        MessageLabel.IsVisible = true;
    }

    private void OnEmployeeSelected(object sender, EventArgs e)
    {
        if (EmployeePicker.SelectedItem is string selectedEmployee && !string.IsNullOrEmpty(selectedEmployee))
        {
            Preferences.Set("SelectedEmployee", selectedEmployee);
            LoadSchedule.IsVisible = true;
        }
    }

    private async void LoadScheduleForEmployeer(object sender, EventArgs e)
    {
        if (EmployeePicker.SelectedItem is string selectedEmployee)
        {
            Preferences.Set("SelectedEmployee", selectedEmployee);
            await Navigation.PushAsync(new EmployeeSchedulePage(selectedEmployee));
        }
        else
        {
            await DisplayAlert("Ошибка", "Пожалуйста, выберите сотрудника", "OK");
        }
    }

    private async void DeleteAllFiles(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Подтверждение", "Вы действительно хотите удалить все данные?", "Да", "Нет");

        if (confirm)
        {
            try
            {
                File.Delete(employeeListFilePath);
                File.Delete(scheduleFilePath);
                Preferences.Clear();

                await AnimateBackToStep1UI();
                await HideDeleteButton();
                DisplayMessage("Все данные были удалены");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось удалить данные: {ex.Message}", "OK");
            }
        }
    }

    private async void GoToSettings(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }
}
