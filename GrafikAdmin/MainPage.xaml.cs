using GrafikAdmin.Services;
using GrafikShared.Services;
using System.Diagnostics;

namespace GrafikAdmin;

public partial class MainPage : ContentPage
{
    private readonly ScheduleStorageService _storageService = new();
    private readonly ExcelExportService _excelService = new();
    private readonly EmployeeStorageService _employeeService = new();

    private static bool _monitorInitialized = false;

    public MainPage()
    {
        InitializeComponent();
        Debug.WriteLine("[MainPage] Конструктор");

        // Подписываемся на обновление счётчика
        FirebaseConnectionMonitor.Instance.PendingSwapsCountChanged += OnPendingSwapsCountChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Debug.WriteLine("[MainPage] OnAppearing");

        // Запускаем мониторинг только ОДИН раз
        if (!_monitorInitialized)
        {
            InitializeFirebaseMonitor();
            _monitorInitialized = true;
        }

        // Обновляем список расписаний
        LoadSchedulesList();

        // Устанавливаем текущее значение счётчика из монитора
        UpdateSwapBadge(FirebaseConnectionMonitor.Instance.PendingSwapsCount);
    }

    /// <summary>
    /// Обработчик изменения счётчика ожидающих запросов
    /// </summary>
    private void OnPendingSwapsCountChanged(object? sender, int count)
    {
        UpdateSwapBadge(count);
    }

    /// <summary>
    /// Обновить бейдж счётчика
    /// </summary>
    private void UpdateSwapBadge(int count)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (count > 0)
            {
                SwapBadgeLabel.Text = count > 99 ? "99+" : count.ToString();
                SwapBadge.IsVisible = true;
            }
            else
            {
                SwapBadge.IsVisible = false;
            }
        });
    }

    private void InitializeFirebaseMonitor()
    {
        Debug.WriteLine("[MainPage] Инициализация мониторинга Firebase...");

        var url = Preferences.Get("FirebaseUrl", string.Empty);

        if (string.IsNullOrEmpty(url))
        {
            var defaultUrl = "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app";
            Preferences.Set("FirebaseUrl", defaultUrl);
            Debug.WriteLine($"[MainPage] Установлен дефолтный URL: {defaultUrl}");
        }

        FirebaseConnectionMonitor.Instance.Start();
        Debug.WriteLine("[MainPage] Мониторинг запущен ✓");
    }

    private void LoadSchedulesList()
    {
        var schedules = _storageService.GetAvailableSchedules();
        SchedulesList.ItemsSource = schedules;
    }

    private async void OnEmployeesClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new EmployeesPage());
    }

    private async void OnSwapRequestsClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SwapRequestsPage());
    }

    private async void OnCreateScheduleClicked(object sender, EventArgs e)
    {
        var employees = await _employeeService.LoadAsync();

        if (employees.TotalCount == 0)
        {
            bool addNow = await DisplayAlert(
                "Нет сотрудников",
                "Сначала добавьте сотрудников в список.\n\nОткрыть список сотрудников?",
                "Да",
                "Отмена");

            if (addNow)
            {
                await Navigation.PushAsync(new EmployeesPage());
            }
            return;
        }

        var today = DateTime.Today;
        var months = new List<string>();

        for (int i = 0; i < 3; i++)
        {
            var date = today.AddMonths(i);
            months.Add(date.ToString("MMMM yyyy"));
        }

        var selected = await DisplayActionSheet("Выберите месяц", "Отмена", null, months.ToArray());

        if (selected != null && selected != "Отмена")
        {
            var index = months.IndexOf(selected);
            var targetDate = today.AddMonths(index);

            await Navigation.PushAsync(new ScheduleEditorPage(targetDate.Year, targetDate.Month));
        }
    }

    private async void OnScheduleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ScheduleInfo schedule)
        {
            await Navigation.PushAsync(new ScheduleEditorPage(schedule.Year, schedule.Month));
            ((CollectionView)sender).SelectedItem = null;
        }
    }

    private async void OnExportExcelClicked(object sender, EventArgs e)
    {
        var schedules = _storageService.GetAvailableSchedules();

        if (schedules.Count == 0)
        {
            await DisplayAlert("Информация", "Нет расписаний для экспорта", "OK");
            return;
        }

        var options = schedules.Select(s => s.DisplayName).ToArray();
        var selected = await DisplayActionSheet("Выберите расписание для экспорта", "Отмена", null, options);

        if (selected != null && selected != "Отмена")
        {
            var scheduleInfo = schedules.First(s => s.DisplayName == selected);
            var schedule = await _storageService.LoadScheduleAsync(scheduleInfo.Year, scheduleInfo.Month);

            if (schedule != null)
            {
                try
                {
                    var filePath = await _excelService.ExportToExcelAsync(schedule);

                    await DisplayAlert("Успех",
                        $"Файл сохранён:\n{Path.GetFileName(filePath)}",
                        "OK");

                    bool share = await DisplayAlert("Поделиться?",
                        "Хотите отправить файл?",
                        "Да", "Нет");

                    if (share)
                    {
                        await Share.Default.RequestAsync(new ShareFileRequest
                        {
                            Title = "Расписание",
                            File = new ShareFile(filePath)
                        });
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Ошибка", $"Не удалось экспортировать: {ex.Message}", "OK");
                }
            }
        }
    }

    private async void OnSendToChatClicked(object sender, EventArgs e)
    {
        var schedules = _storageService.GetAvailableSchedules();

        if (schedules.Count == 0)
        {
            await DisplayAlert("Информация", "Сначала создайте расписание", "OK");
            return;
        }

        var options = schedules.Select(s => s.DisplayName).ToArray();
        var selected = await DisplayActionSheet("Выберите расписание для отправки", "Отмена", null, options);

        if (selected != null && selected != "Отмена")
        {
            var scheduleInfo = schedules.First(s => s.DisplayName == selected);
            var schedule = await _storageService.LoadScheduleAsync(scheduleInfo.Year, scheduleInfo.Month);

            if (schedule != null)
            {
                try
                {
                    var filePath = await _excelService.ExportToExcelAsync(schedule);

                    var firebaseUrl = Preferences.Get("FirebaseUrl",
                        "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");

                    var firebaseService = new GrafikShared.Services.FirebaseServiceBase(firebaseUrl);
                    var success = await firebaseService.SendFileAsync("Администратор", filePath);

                    if (success)
                    {
                        await DisplayAlert("Успех", "Расписание отправлено в чат!", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Ошибка", "Не удалось отправить в чат", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Ошибка", $"Ошибка: {ex.Message}", "OK");
                }
            }
        }
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage());
    }

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        await DisplayAlert("О приложении",
            "График Администратор v1.0\n\n" +
            "Приложение для составления\nрасписания работы сотрудников.\n\n" +
            "• Постоянный список сотрудников\n" +
            "• Хранение до 3 месяцев\n" +
            "• Экспорт в Excel\n" +
            "• Отправка через чат",
            "OK");
    }
}