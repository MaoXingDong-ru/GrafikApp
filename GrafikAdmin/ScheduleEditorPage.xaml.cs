using GrafikAdmin.Models;
using GrafikAdmin.Services;

namespace GrafikAdmin;

public partial class ScheduleEditorPage : ContentPage
{
    private readonly int _year;
    private readonly int _month;
    private readonly ScheduleStorageService _storageService = new();
    private readonly ExcelExportService _excelService = new();
    private readonly EmployeeStorageService _employeeService = new();
    private MonthlySchedule _schedule = new();
    private readonly Dictionary<(string employee, DateTime date), ShiftType> _shiftData = [];

    private static FirebaseScheduleService? _firebaseService;

    public ScheduleEditorPage(int year, int month)
    {
        InitializeComponent();
        _year = year;
        _month = month;
        MonthLabel.Text = new DateTime(year, month, 1).ToString("MMMM yyyy");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadScheduleAsync();
        BuildGrid();
    }

    private async Task LoadScheduleAsync()
    {
        var existing = await _storageService.LoadScheduleAsync(_year, _month);

        if (existing != null)
        {
            _schedule = existing;

            foreach (var entry in _schedule.Entries)
            {
                _shiftData[(entry.EmployeeName, entry.Date.Date)] = entry.ShiftType;
            }
        }
        else
        {
            _schedule = new MonthlySchedule
            {
                Year = _year,
                Month = _month,
                CreatedAt = DateTime.UtcNow
            };
        }

        var employees = await _employeeService.LoadAsync();
        _schedule.Employees = employees.All;
        _schedule.SecondLineEmployees = employees.SecondLine;
    }

    /// <summary>
    /// Сохранить расписание в хранилище
    /// </summary>
    private async Task SaveScheduleAsync()
    {
        _schedule.Entries.Clear();

        foreach (var kvp in _shiftData)
        {
            _schedule.Entries.Add(new ScheduleEntry
            {
                EmployeeName = kvp.Key.employee,
                Date = kvp.Key.date,
                ShiftType = kvp.Value,
                IsSecondLine = _schedule.SecondLineEmployees.Contains(kvp.Key.employee)
            });
        }

        await _storageService.SaveScheduleAsync(_schedule);
    }

    private void BuildGrid()
    {
        // Очищаем оба Grid'а
        ScheduleGrid.Children.Clear();
        ScheduleGrid.ColumnDefinitions.Clear();
        ScheduleGrid.RowDefinitions.Clear();

        EmployeesColumn.Children.Clear();
        EmployeesColumn.RowDefinitions.Clear();

        var daysInMonth = DateTime.DaysInMonth(_year, _month);
        var employees = _schedule.Employees;

        if (employees.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "Нет сотрудников.\nДобавьте их в разделе «Список сотрудников»",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                FontSize = 16,
                TextColor = Colors.Gray
            };
            ScheduleGrid.Children.Add(emptyLabel);
            return;
        }

        // === Левый столбец (закреплённые имена сотрудников) ===
        EmployeesColumn.RowDefinitions.Add(new RowDefinition(new GridLength(40))); // Заголовок
        foreach (var _ in employees)
            EmployeesColumn.RowDefinitions.Add(new RowDefinition(new GridLength(45)));

        // Заголовок "Сотрудник"
        var headerLabel = new Label
        {
            Text = "Сотрудник",
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2D2D2D")
        };
        Grid.SetRow(headerLabel, 0);
        EmployeesColumn.Children.Add(headerLabel);

        // Имена сотрудников
        for (int i = 0; i < employees.Count; i++)
        {
            var employee = employees[i];
            int row = i + 1;

            var nameLabel = new Label
            {
                Text = employee,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(5, 0),
                TextColor = _schedule.SecondLineEmployees.Contains(employee)
                    ? Color.FromArgb("#C8E6C9")
                    : Colors.White,
                BackgroundColor = Color.FromArgb("#1E1E1E")
            };
            Grid.SetRow(nameLabel, row);
            EmployeesColumn.Children.Add(nameLabel);
        }

        // === Правая часть (прокручиваемые даты и смены) ===
        for (int d = 1; d <= daysInMonth; d++)
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(45)));

        ScheduleGrid.RowDefinitions.Add(new RowDefinition(new GridLength(40))); // Заголовок дат
        foreach (var _ in employees)
            ScheduleGrid.RowDefinitions.Add(new RowDefinition(new GridLength(45)));

        // Заголовки дат
        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateTime(_year, _month, d);
            var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

            var dateLabel = new Label
            {
                Text = d.ToString(),
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                TextColor = isWeekend ? Color.FromArgb("#FFCDD2") : Colors.White
            };
            Grid.SetRow(dateLabel, 0);
            Grid.SetColumn(dateLabel, d - 1); // Индекс с 0
            ScheduleGrid.Children.Add(dateLabel);
        }

        // Ячейки смен
        for (int i = 0; i < employees.Count; i++)
        {
            var employee = employees[i];
            int row = i + 1;

            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(_year, _month, d);
                var shiftType = _shiftData.GetValueOrDefault((employee, date), ShiftType.DayOff);

                var cellButton = new Button
                {
                    Text = shiftType.ToShortString(),
                    BackgroundColor = shiftType.ToColor(),
                    TextColor = Colors.White,
                    FontSize = 10,
                    Padding = 0,
                    CornerRadius = 5,
                    CommandParameter = (employee, date)
                };
                cellButton.Clicked += OnCellClicked;

                Grid.SetRow(cellButton, row);
                Grid.SetColumn(cellButton, d - 1); // Индекс с 0
                ScheduleGrid.Children.Add(cellButton);
            }
        }
    }
    private async void OnCellClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is (string employee, DateTime date))
        {
            var options = Enum.GetValues<ShiftType>()
                .Select(s => s.ToDisplayString())
                .ToArray();

            var selected = await DisplayActionSheet(
                $"{employee} — {date:dd.MM}",
                "Отмена",
                null,
                options);

            if (selected != null && selected != "Отмена")
            {
                var shiftType = Enum.GetValues<ShiftType>()
                    .First(s => s.ToDisplayString() == selected);

                _shiftData[(employee, date)] = shiftType;

                button.Text = shiftType.ToShortString();
                button.BackgroundColor = shiftType.ToColor();
            }
        }
    }

    /// <summary>
    /// Сохранить только локально
    /// </summary>
    private async void OnSaveClicked(object sender, EventArgs e)
    {
        await SaveScheduleAsync();
        await DisplayAlert("✅ Сохранено", "Расписание сохранено локально.", "OK");
    }

    /// <summary>
    /// Выгрузить в чат Firebase
    /// </summary>
    private async void OnUploadToChatClicked(object sender, EventArgs e)
    {
        await SaveScheduleAsync();

        var firebaseUrl = Preferences.Get("FirebaseUrl", string.Empty);

        if (string.IsNullOrEmpty(firebaseUrl))
        {
            await DisplayAlert("⚠️ Ошибка",
                "Firebase URL не настроен.\n\nПерейдите в Настройки и укажите URL базы данных.",
                "OK");
            return;
        }

        var confirm = await DisplayAlert("📤 Выгрузка в чат",
            $"Выгрузить расписание «{_schedule.DisplayName}» в чат?\n\nСообщение будет автоматически закреплено.",
            "Выгрузить", "Отмена");

        if (!confirm)
            return;

        _firebaseService ??= new FirebaseScheduleService(firebaseUrl);

        var uploaded = await _firebaseService.UploadScheduleToChatAsync(_schedule);

        if (uploaded)
        {
            await DisplayAlert("✅ Успех",
                $"Расписание выгружено в чат!\n\n📁 {_schedule.Month:D2}_{_schedule.Year}.json\n📌 Сообщение закреплено",
                "OK");
        }
        else
        {
            await DisplayAlert("❌ Ошибка",
                "Не удалось выгрузить расписание в чат.\n\nПопробуйте ещё раз.",
                "OK");
        }
    }

    /// <summary>
    /// Экспорт в Excel
    /// </summary>
    private async void OnExportClicked(object sender, EventArgs e)
    {
        await SaveScheduleAsync();

        try
        {
            var filePath = await _excelService.ExportToExcelAsync(_schedule);

            var action = await DisplayActionSheet(
                $"Файл сохранён:\n{Path.GetFileName(filePath)}",
                "Закрыть",
                null,
                "📤 Поделиться",
                "📂 Показать путь");

            if (action == "📤 Поделиться")
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = $"Расписание {_schedule.DisplayName}",
                    File = new ShareFile(filePath)
                });
            }
            else if (action == "📂 Показать путь")
            {
                await DisplayAlert("Путь к файлу", filePath, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", ex.Message, "OK");
        }
    }
}