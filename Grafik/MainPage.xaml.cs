using ClosedXML.Excel;
using Microsoft.Maui.Storage;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Grafik.Services;

namespace Grafik;

public partial class MainPage : ContentPage
{
    private readonly string employeeListFilePath = Path.Combine(FileSystem.AppDataDirectory, "employees.json");

    private static bool _monitorInitialized = false;

    public MainPage()
    {
        InitializeComponent();
        SetupInitialState();
        LoadEmployeeList();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Запускаем мониторинг только ОДИН раз
        if (!_monitorInitialized)
        {
            InitializeFirebaseMonitor();
            _monitorInitialized = true;
        }

        // Подписываемся на изменение статуса соединения
        FirebaseConnectionMonitor.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Устанавливаем текущее состояние кнопки сразу
        ChatButton.IsVisible = FirebaseConnectionMonitor.Instance.IsConnected;

        // Проверяем, есть ли сохранённый файл для загрузки из чата
        await CheckAndLoadPendingScheduleFileAsync();

        // Сводка на главной
        await RefreshDashboardAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Отписываемся, чтобы не обновлять UI неактивной страницы
        FirebaseConnectionMonitor.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;
    }

    // Обработчик изменения статуса соединения — кнопка чата и плитка чата
    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ChatButton.IsVisible = isConnected;
            UpdateChatStatus(isConnected);

            if (isConnected)
                _ = RefreshUnreadAsync();

            Console.WriteLine($"[MainPage] Кнопка чата: {(isConnected ? "видима ✓" : "скрыта ✗")}");
        });
    }

    // Инициализирует и запускает FirebaseConnectionMonitor
    private static void InitializeFirebaseMonitor()
    {
        Console.WriteLine("[MainPage] Инициализация мониторинга Firebase...");

        var url = Preferences.Get("FirebaseUrl", string.Empty);

        if (string.IsNullOrEmpty(url))
        {
            const string defaultUrl = "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app";
            Preferences.Set("FirebaseUrl", defaultUrl);
            Console.WriteLine($"[MainPage] Установлен дефолтный URL: {defaultUrl}");
        }

        FirebaseConnectionMonitor.Instance.Start();
        Console.WriteLine("[MainPage] Мониторинг запущен ✓");
    }

    private async void GoToChat(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ChatPage());
    }

    private async void OnChatTileTapped(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ChatPage());
    }

    // ───────────── Сводка на главной ─────────────

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var employee = Preferences.Get("SelectedEmployee", string.Empty);
            var slots = await Task.Run(ScheduleSlotService.GetSlots);

            if (string.IsNullOrEmpty(employee) || slots.Count == 0)
            {
                DashboardGroup.IsVisible = false;
                return;
            }

            var all = await Task.Run(ScheduleSlotService.LoadAll);
            var now = DateTime.Now;

            // Ближайшая смена
            var next = ScheduleInsights.GetNextShift(all, employee, now);
            if (next == null)
            {
                HomeNextTitleLabel.Text = "Ближайшая смена";
                HomeNextTextLabel.Text = "Предстоящих смен нет";
                HomeNextCountdownLabel.Text = string.Empty;
            }
            else
            {
                HomeNextTitleLabel.Text = next.IsActive ? "Сейчас на смене" : "Ближайшая смена";
                HomeNextTextLabel.Text =
                    $"{ScheduleInsights.DayLabel(next.Start, now)} · {next.Entry.Shift} · {next.Entry.Worktime}";
                HomeNextCountdownLabel.Text = ScheduleInsights.Countdown(next, now);
            }

            // Сегодня на смене
            var today = ScheduleInsights.DescribeWorkingOn(all, now, employee);
            HomeTodayCard.IsVisible = !string.IsNullOrEmpty(today);
            HomeTodayLabel.Text = today;

            BuildSlotTiles(employee, slots, all);
            UpdateChatStatus(FirebaseConnectionMonitor.Instance.IsConnected);
            DashboardGroup.IsVisible = true;

            await RefreshUnreadAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainPage] Ошибка обновления сводки: {ex.Message}");
        }
    }

    private void BuildSlotTiles(string employee, List<ScheduleMonth> slots, List<ShiftEntry> all)
    {
        SlotTilesGrid.Children.Clear();
        SlotTilesGrid.ColumnDefinitions.Clear();

        var currentMonth = new ScheduleMonth(DateTime.Today.Year, DateTime.Today.Month);

        for (int i = 0; i < slots.Count; i++)
        {
            var month = slots[i];

            int shifts = all
                .Where(e => string.Equals(e.Employees, employee, StringComparison.OrdinalIgnoreCase)
                            && month.Contains(e.Date)
                            && !string.IsNullOrWhiteSpace(e.Shift))
                .Select(e => e.Date.Date)
                .Distinct()
                .Count();

            SlotTilesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var tile = new Frame
            {
                Padding = new Thickness(8, 10),
                CornerRadius = 12,
                HasShadow = false,
                BackgroundColor = Color.FromArgb("#2D2D2D"),
                BorderColor = Color.FromArgb(month == currentMonth ? "#4CAF50" : "#3D3D3D"),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = month.MonthName, FontSize = 14, FontAttributes = FontAttributes.Bold,
                                    TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = month.Year.ToString(), FontSize = 12,
                                    TextColor = Color.FromArgb("#B0B0B0"), HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = $"{shifts} смен", FontSize = 12,
                                    TextColor = Color.FromArgb("#4CAF50"), HorizontalTextAlignment = TextAlignment.Center }
                    }
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await Navigation.PushAsync(new EmployeeSchedulePage(employee, month));
            tile.GestureRecognizers.Add(tap);

            SlotTilesGrid.Add(tile, i, 0);
        }
    }

    private void UpdateChatStatus(bool connected)
    {
        ChatStatusDot.TextColor = connected ? Colors.LimeGreen : Colors.OrangeRed;
        ChatStatusLabel.Text = connected ? "Чат подключён" : "Чат недоступен — нет соединения";

        if (!connected)
            ChatUnreadLabel.Text = string.Empty;
    }

    private async Task RefreshUnreadAsync()
    {
        if (!FirebaseConnectionMonitor.Instance.IsConnected)
            return;

        int? unread = await ChatUnreadService.GetUnreadCountAsync();

        ChatUnreadLabel.Text = unread switch
        {
            > 0 => $"Новых сообщений: {unread}",
            0 => "Новых сообщений нет",
            _ => string.Empty
        };
    }

    // ───────────── Загрузка расписания ─────────────

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

            if (result == null)
                return;

            var bytes = await File.ReadAllBytesAsync(result.FullPath);

            // Единое подтверждение: какой месяц загружается и какой будет удалён
            if (!await ConfirmScheduleLoadAsync(this, bytes))
                return;

            await ProcessExcelFile(bytes);
        }
        catch (Exception ex)
        {
            DisplayMessage($"Ошибка: {ex.Message}");
        }
    }

    /// <summary>Подтверждение загрузки (используется и для файла, и для чата).</summary>
    public static async Task<bool> ConfirmScheduleLoadAsync(Page page, byte[] fileBytes)
    {
        var plan = ScheduleSlotService.Inspect(ExtractScheduleFromExcel(fileBytes));

        if (plan == null)
        {
            await page.DisplayAlert("Ошибка", "Не удалось определить месяц расписания в файле.", "OK");
            return false;
        }

        string message = plan.ReplacesSame
            ? $"Будет загружено расписание «{plan.NewMonth.Title}».\nОно заменит ранее загруженное за этот месяц."
            : plan.Evicted is ScheduleMonth old
                ? $"Будет загружено расписание «{plan.NewMonth.Title}».\nПри этом старый файл «{old.Title}» будет удалён."
                : $"Будет загружено расписание «{plan.NewMonth.Title}».";

        return await page.DisplayAlert("Загрузка расписания", message, "Загрузить", "Отмена");
    }

    private async Task ProcessExcelFile(byte[] fileBytes)
    {
        var schedule = ExtractScheduleFromExcel(fileBytes);

        // Кладём в слот месяца; при переполнении самый старый слот удаляется
        ScheduleSlotService.Save(schedule);

        // Сотрудники — из всех слотов
        var uniqueEmployees = ScheduleSlotService.GetEmployees();
        SaveEmployeesToJson(uniqueEmployees);

        if (uniqueEmployees.Count > 0)
        {
            EmployeePicker.ItemsSource = uniqueEmployees;

            if (!SecondStepGroup.IsVisible)
                await AnimateToStep2UI();

            await ShowDeleteButton();
            MessageLabel.IsVisible = false;

            await RefreshDashboardAsync();
        }
        else
        {
            DisplayMessage("Не удалось извлечь сотрудников.");
        }
    }

    private async Task ProcessExcelFile(string filePath)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        await ProcessExcelFile(fileBytes);
    }

    // Публичный метод для загрузки расписания из файла (вызывается из ChatPage;
    // подтверждение уже получено на стороне чата)
    public async Task ProcessExcelFileAsync(string filePath)
    {
        try
        {
            await ProcessExcelFile(filePath);
        }
        catch (Exception ex)
        {
            DisplayMessage($"Ошибка при загрузке файла: {ex.Message}");
            throw;
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

    // ───────────── Разбор Excel ─────────────

    public static List<ShiftEntry> ExtractScheduleFromExcel(byte[] fileData)
    {
        var result = new List<ShiftEntry>();
        using var stream = new MemoryStream(fileData);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var invalidNames = new HashSet<string>
        {
            "дата", "время", "date", "time", "",
            "архипов дмитрий", "брусниги н антон"
        };

        var dayColumns = GetValidDayColumns(worksheet);

        var (firstLineCount, secondLineCount, secondLineStartRow) = DetectEmployeeCounts(worksheet);

        int startRowFirstLine = 4;
        int firstLineEndRow = ProcessEmployeeGroup(worksheet, startRowFirstLine, firstLineCount,
                                                 result, invalidNames, false, dayColumns);

        if (secondLineCount > 0)
        {
            ProcessEmployeeGroup(worksheet, secondLineStartRow, secondLineCount,
                               result, invalidNames, true, dayColumns);
        }

        Console.WriteLine($"Обнаружено: первая линия = {firstLineCount}, вторая линия = {secondLineCount}, начало второй линии = {secondLineStartRow}");

        return result;
    }

    private static (int firstLineCount, int secondLineCount, int secondLineStartRow) DetectEmployeeCounts(IXLWorksheet worksheet)
    {
        const int startRow = 4;
        var invalidNames = new HashSet<string>
        {
            "дата", "время", "date", "time", "",
            "архипов дмитрий", "брусниги н антон"
        };

        int firstLineCount = 0;
        int secondLineCount = 0;
        int secondLineStartRow = 0;
        int currentRow = startRow;
        bool foundFirstLine = false;
        bool foundGap = false;
        int emptyRowsCount = 0;
        const int emptyRowsThreshold = 2;

        var lastUsedCell = worksheet.LastRowUsed();
        if (lastUsedCell == null)
            return (0, 0, 0);

        int lastRow = lastUsedCell.RowNumber();

        while (currentRow <= lastRow)
        {
            var nameCell = worksheet.Cell(currentRow, 1);
            string rawName = nameCell.GetString().Trim().ToLower();

            if (string.IsNullOrWhiteSpace(rawName) || invalidNames.Contains(rawName))
            {
                emptyRowsCount++;

                if (foundFirstLine && !foundGap && emptyRowsCount >= emptyRowsThreshold)
                {
                    foundGap = true;
                    currentRow++;
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
                    secondLineStartRow = currentRow;
                    break;
                }
                currentRow++;
            }
            else
            {
                emptyRowsCount = 0;
                foundFirstLine = true;
                firstLineCount++;
                currentRow++;
            }
        }

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
                currentRow++;
            }
            else
            {
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

        var exclusionWords = new[] { "отпуск", "замещает", "выходной", "вых" };

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

    // ───────────── Сохранение / загрузка списков ─────────────

    private void SaveEmployeesToJson(List<string> employees)
    {
        var json = JsonSerializer.Serialize(employees, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(employeeListFilePath, json);
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

    // Публичный метод для удаления всех данных (вызывается из UI)
    public async Task ClearAllDataAsync()
    {
        try
        {
            // Удаляет все слоты и объединённый schedule.json
            ScheduleSlotService.ClearAll();

            if (File.Exists(employeeListFilePath))
                File.Delete(employeeListFilePath);

            Preferences.Remove("SelectedEmployee");
            Preferences.Remove("SelectedScheduleMonth");
            Preferences.Remove("PendingScheduleFile");

            DashboardGroup.IsVisible = false;

            await AnimateBackToStep1UI();
            await HideDeleteButton();
            DisplayMessage("Все данные были удалены");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainPage] Ошибка при удалении данных: {ex.Message}");
            throw;
        }
    }

    private async void DeleteAllFiles(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Подтверждение", "Вы действительно хотите удалить все данные?", "Да", "Нет");

        if (confirm)
        {
            try
            {
                await ClearAllDataAsync();
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

    private void SetupInitialState()
    {
        DeleteData.IsVisible = false;
        DeleteData.TranslationX = -120;

        // Кнопка чата скрыта до получения первого статуса от монитора
        ChatButton.IsVisible = false;

        string selectedEmployee = Preferences.Get("SelectedEmployee", string.Empty);
        if (!string.IsNullOrEmpty(selectedEmployee))
        {
            Navigation.PushAsync(new EmployeeSchedulePage(selectedEmployee));
        }
    }

    // Файл из чата уже подтверждён пользователем в ChatPage — грузим без повторных вопросов
    private async Task CheckAndLoadPendingScheduleFileAsync()
    {
        try
        {
            var pendingFile = Preferences.Get("PendingScheduleFile", string.Empty);
            if (string.IsNullOrEmpty(pendingFile))
                return;

            Preferences.Remove("PendingScheduleFile");

            if (!File.Exists(pendingFile))
                return;

            try
            {
                await ProcessExcelFileAsync(pendingFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainPage] Ошибка при загрузке: {ex.Message}");
                await DisplayAlert("❌ Ошибка", $"Не удалось загрузить расписание:\n{ex.Message}", "OK");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainPage] Ошибка при проверке ожидающего файла: {ex.Message}");
        }
    }
}