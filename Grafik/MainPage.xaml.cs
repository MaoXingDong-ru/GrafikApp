using ClosedXML.Excel;
using Microsoft.Maui.Storage;
using System.Globalization;
using System.Runtime.InteropServices;
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

        var schedule = ExtractScheduleFromExcel(fileBytes);
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

    public static List<ShiftEntry> ExtractScheduleFromExcel(byte[] fileData)
    {
        var result = new List<ShiftEntry>();
        using var stream = new MemoryStream(fileData);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var invalidNames = new HashSet<string>
        {
            "дата", "время", "date", "time", "",
            "архипов дмитрий", "бруснигин антон" // Исключаем зарубежную техподдержку
        };

        var dayColumns = GetValidDayColumns(worksheet);

        // Автоматически определяем количество сотрудников
        var (firstLineCount, secondLineCount, secondLineStartRow) = DetectEmployeeCounts(worksheet);

        int startRowFirstLine = 4;
        int firstLineEndRow = ProcessEmployeeGroup(worksheet, startRowFirstLine, firstLineCount,
                                                 result, invalidNames, false, dayColumns);

        if (secondLineCount > 0)
        {
            // Используем реально найденную строку начала второй линии
            ProcessEmployeeGroup(worksheet, secondLineStartRow, secondLineCount,
                               result, invalidNames, true, dayColumns);
        }

        Console.WriteLine($"Обнаружено: первая линия = {firstLineCount}, вторая линия = {secondLineCount}, начало второй линии = {secondLineStartRow}");

        return result;
    }

    /// <summary>
    /// Автоматически определяет количество сотрудников первой и второй линии
    /// </summary>
    private static (int firstLineCount, int secondLineCount, int secondLineStartRow) DetectEmployeeCounts(IXLWorksheet worksheet)
    {
        const int startRow = 4;
        var invalidNames = new HashSet<string> 
        { 
            "дата", "время", "date", "time", "",
            "архипов дмитрий", "бруснигин антон" // Исключаем зарубежную техподдержку
        };

        int firstLineCount = 0;
        int secondLineCount = 0;
        int secondLineStartRow = 0;
        int currentRow = startRow;
        bool foundFirstLine = false;
        bool foundGap = false;
        int emptyRowsCount = 0;
        const int emptyRowsThreshold = 2; // Если подряд 2+ пустые строки - это разделитель
        
        var lastUsedCell = worksheet.LastRowUsed();
        if (lastUsedCell == null)
            return (0, 0, 0);

        int lastRow = lastUsedCell.RowNumber();

        // Ищем первую линию и её длину
        while (currentRow <= lastRow)
        {
            var nameCell = worksheet.Cell(currentRow, 1);
            string rawName = nameCell.GetString().Trim().ToLower();

            if (string.IsNullOrWhiteSpace(rawName) || invalidNames.Contains(rawName))
            {
                emptyRowsCount++;
                
                // Если мы уже нашли первую линию и встретили достаточно пустых строк
                if (foundFirstLine && !foundGap && emptyRowsCount >= emptyRowsThreshold)
                {
                    foundGap = true;
                    currentRow++;
                    // Пропускаем пропуск между линиями
                    while (currentRow <= lastRow)
                    {
                        string nextName = worksheet.Cell(currentRow, 1).GetString().Trim().ToLower();
                        if (string.IsNullOrWhiteSpace(nextName) || invalidNames.Contains(nextName))
                        {
                            currentRow++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    // Теперь currentRow указывает на начало второй линии
                    secondLineStartRow = currentRow;
                    break;
                }
                currentRow++;
            }
            else
            {
                emptyRowsCount = 0; // Сбрасываем счётчик пустых строк
                foundFirstLine = true;
                firstLineCount++;
                currentRow++;
            }
        }

        // Считаем вторую линию
        while (currentRow <= lastRow)
        {
            var nameCell = worksheet.Cell(currentRow, 1);
            string rawName = nameCell.GetString().Trim().ToLower();

            if (!string.IsNullOrWhiteSpace(rawName) && !invalidNames.Contains(rawName))
            {
                secondLineCount++;
                currentRow++;
            }
            else if (!string.IsNullOrWhiteSpace(rawName) && invalidNames.Contains(rawName))
            {
                // Пропускаем исключённых сотрудников (зарубежную поддержку)
                currentRow++;
            }
            else
            {
                // Если встретили полностью пустую строку
                break;
            }
        }

        Console.WriteLine($"DetectEmployeeCounts: первая линия = {firstLineCount}, вторая линия = {secondLineCount}, начало второй = {secondLineStartRow}");

        return (firstLineCount, secondLineCount, secondLineStartRow);
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
        var exclusionWords = new[] { "отпуск", "замещает", "выходной" };

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

                bool shouldExclude = exclusionWords.Any(keyword =>
                    dayShift.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nightShift.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!shouldExclude && (!string.IsNullOrWhiteSpace(dayShift) || !string.IsNullOrWhiteSpace(nightShift)))
                {
                    result.Add(new ShiftEntry
                    {
                        Employees = name,
                        Date = date,
                        Shift = $"{(string.IsNullOrEmpty(dayShift) ? "" : "Дневная")} {(string.IsNullOrEmpty(nightShift) ? "" : "Ночная")}".Trim(),
                        Worktime = $"{(string.IsNullOrEmpty(dayShift) ? "" : GetLocalWorktime(true))} {(string.IsNullOrEmpty(nightShift) ? "" : GetLocalWorktime(false))}".Trim(),
                        IsSecondLine = isSecondLine
                    });
                }
            }

            lastProcessedRow = row;
        }

        return lastProcessedRow;
    }

    private static string GetLocalWorktime(bool isDayShift)
    {
        TimeZoneInfo moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Russian Standard Time" : "Europe/Moscow");

        DateTime todayMoscow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, moscowTimeZone).Date;

        DateTime startMoscow, endMoscow;

        if (isDayShift)
        {
            startMoscow = todayMoscow.AddHours(9);
            endMoscow = todayMoscow.AddHours(21);
        }
        else
        {
            startMoscow = todayMoscow.AddHours(21);
            endMoscow = todayMoscow.AddDays(1).AddHours(9);
        }

        startMoscow = DateTime.SpecifyKind(startMoscow, DateTimeKind.Unspecified);
        endMoscow = DateTime.SpecifyKind(endMoscow, DateTimeKind.Unspecified);

        DateTime startLocal = TimeZoneInfo.ConvertTime(startMoscow, moscowTimeZone, TimeZoneInfo.Local);
        DateTime endLocal = TimeZoneInfo.ConvertTime(endMoscow, moscowTimeZone, TimeZoneInfo.Local);

        return $"{startLocal:HH:mm}-{endLocal:HH:mm}";
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

    private void OnTestImmediateNotificationClicked(object sender, EventArgs e)
    {
#if ANDROID
    Grafik.Services.NotificationService.ShowTestNotification();
    DisplayAlert("Тест", "Мгновенное уведомление отправлено", "OK");
#else
        DisplayAlert("Тест", "Уведомления доступны только на Android", "OK");
#endif
    }

    private void OnTestScheduledNotificationClicked(object sender, EventArgs e)
    {
#if ANDROID
    Grafik.Services.NotificationService.ScheduleTestNotification();
    DisplayAlert("Тест", "Уведомление запланировано на 10 секунд", "OK");
#else
        DisplayAlert("Тест", "Планирование доступно только на Android", "OK");
#endif
    }

    private async void GoToSettings(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }
}
