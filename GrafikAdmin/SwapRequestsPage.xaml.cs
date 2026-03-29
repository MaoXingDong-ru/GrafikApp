using GrafikAdmin.Models;
using GrafikAdmin.Services;
using GrafikShared.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace GrafikAdmin;

public partial class SwapRequestsPage : ContentPage
{
    private ShiftSwapService? _swapService;
    private readonly ScheduleStorageService _storageService = new();
    private readonly ObservableCollection<ShiftSwapRequest> _requests = [];

    public SwapRequestsPage()
    {
        InitializeComponent();
        RequestsCollection.ItemsSource = _requests;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadRequestsAsync();
    }

    private ShiftSwapService GetSwapService()
    {
        _swapService ??= new ShiftSwapService(
            Preferences.Get("FirebaseUrl",
                "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/"));
        return _swapService;
    }

    private async Task LoadRequestsAsync()
    {
        try
        {
            _requests.Clear();
            var allRequests = await GetSwapService().GetAllRequestsAsync();

            var sorted = allRequests
                .OrderBy(r => r.Status == "pending" ? 0 : 1)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();

            foreach (var request in sorted)
            {
                _requests.Add(request);
            }

            var pendingCount = sorted.Count(r => r.Status == "pending");
            CountLabel.Text = pendingCount > 0
                ? $"Ожидают обработки: {pendingCount}"
                : "Все запросы обработаны";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwapRequestsPage] Ошибка загрузки: {ex.Message}");
            await DisplayAlert("Ошибка", "Не удалось загрузить запросы", "OK");
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadRequestsAsync();
        RefreshViewControl.IsRefreshing = false;
    }

    private async void OnApproveClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ShiftSwapRequest request)
            return;

        bool confirm = await DisplayAlert("Подтверждение",
            $"Одобрить обмен?\n\n{request.RequesterName} ({request.RequesterDateDisplay}, {request.RequesterShift})\n↔️\n{request.TargetName} ({request.TargetDateDisplay}, {request.TargetShift})",
            "Одобрить", "Отмена");

        if (!confirm) return;

        var comment = GetCommentFromParent(button);

        try
        {
            bool swapSuccess = await ApplySwapToScheduleAsync(request);

            if (!swapSuccess)
            {
                bool continueAnyway = await DisplayAlert("Предупреждение",
                    "Не удалось применить обмен к локальному расписанию.\n\nВсё равно одобрить запрос?",
                    "Да", "Нет");

                if (!continueAnyway) return;
            }

            var success = await GetSwapService().ApproveRequestAsync(request.FirebaseKey, comment);

            if (success)
            {
                await DisplayAlert("✅ Успех", "Запрос одобрен, смены поменяны!", "OK");
                await LoadRequestsAsync();
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось обновить статус", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwapRequestsPage] Ошибка одобрения: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось одобрить: {ex.Message}", "OK");
        }
    }

    private async void OnDenyClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ShiftSwapRequest request)
            return;

        var reason = await DisplayPromptAsync("Причина отказа",
            "Укажите причину (необязательно):",
            "Отклонить", "Отмена",
            placeholder: "Например: конфликт смен");

        if (reason == null) return;

        try
        {
            var success = await GetSwapService().DenyRequestAsync(request.FirebaseKey, reason);

            if (success)
            {
                await DisplayAlert("Готово", "Запрос отклонён", "OK");
                await LoadRequestsAsync();
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось отклонить запрос", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwapRequestsPage] Ошибка отклонения: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось отклонить: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Применить обмен сменами к расписанию.
    /// 
    /// ЛОГИКА ОБМЕНА:
    /// Крылов (3 апреля, Дневная) меняется с Платонов (1 апреля, Ночная)
    /// 
    /// После обмена:
    /// - Крылов работает 1 апреля НОЧНУЮ (вместо Платонова)
    /// - Платонов работает 3 апреля ДНЕВНУЮ (вместо Крылова)
    /// </summary>
    private async Task<bool> ApplySwapToScheduleAsync(ShiftSwapRequest request)
    {
        try
        {
            Debug.WriteLine($"[SwapRequestsPage] === НАЧАЛО ОБМЕНА ===");
            Debug.WriteLine($"[SwapRequestsPage] {request.RequesterName}: {request.RequesterDate:dd.MM} ({request.RequesterShift})");
            Debug.WriteLine($"[SwapRequestsPage] {request.TargetName}: {request.TargetDate:dd.MM} ({request.TargetShift})");

            var requesterMonth = request.RequesterDate.Month;
            var requesterYear = request.RequesterDate.Year;
            var targetMonth = request.TargetDate.Month;
            var targetYear = request.TargetDate.Year;

            var schedule1 = await _storageService.LoadScheduleAsync(requesterYear, requesterMonth);

            if (schedule1 == null)
            {
                Debug.WriteLine($"[SwapRequestsPage] ❌ Расписание {requesterYear}/{requesterMonth} не найдено");
                return false;
            }

            Debug.WriteLine($"[SwapRequestsPage] ✅ Загружено расписание {schedule1.DisplayName}, записей: {schedule1.Entries.Count}");

            if (requesterYear == targetYear && requesterMonth == targetMonth)
            {
                var success = SwapShiftsInSingleSchedule(schedule1, request);
                if (success)
                {
                    await _storageService.SaveScheduleAsync(schedule1);
                    Debug.WriteLine($"[SwapRequestsPage] ✅ Расписание сохранено");
                }
                return success;
            }
            else
            {
                var schedule2 = await _storageService.LoadScheduleAsync(targetYear, targetMonth);

                if (schedule2 == null)
                {
                    Debug.WriteLine($"[SwapRequestsPage] ❌ Расписание {targetYear}/{targetMonth} не найдено");
                    return false;
                }

                var success = SwapShiftsBetweenSchedules(schedule1, schedule2, request);
                if (success)
                {
                    await _storageService.SaveScheduleAsync(schedule1);
                    await _storageService.SaveScheduleAsync(schedule2);
                    Debug.WriteLine($"[SwapRequestsPage] ✅ Оба расписания сохранены");
                }
                return success;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwapRequestsPage] ❌ Исключение: {ex.Message}");
            Debug.WriteLine($"[SwapRequestsPage] Stack: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Поменять смены в рамках одного месяца.
    /// 
    /// Пример: Крылов (3 апреля, Д) ↔️ Платонов (1 апреля, Н)
    /// 
    /// Результат:
    /// - Запись "Крылов, 3 апреля, Д" → становится "Платонов, 3 апреля, Д"
    /// - Запись "Платонов, 1 апреля, Н" → становится "Крылов, 1 апреля, Н"
    /// 
    /// Т.е. меняем ТОЛЬКО имена сотрудников, тип смены остаётся привязан к дате!
    /// </summary>
    private bool SwapShiftsInSingleSchedule(MonthlySchedule schedule, ShiftSwapRequest request)
    {
        Debug.WriteLine($"[SwapRequestsPage] === Поиск записей ===");

        // Запись Requester на его дату (Крылов 3 апреля)
        var requesterEntry = schedule.Entries.FirstOrDefault(e =>
            e.EmployeeName.Equals(request.RequesterName, StringComparison.OrdinalIgnoreCase) &&
            e.Date.Date == request.RequesterDate.Date);

        // Запись Target на его дату (Платонов 1 апреля)
        var targetEntry = schedule.Entries.FirstOrDefault(e =>
            e.EmployeeName.Equals(request.TargetName, StringComparison.OrdinalIgnoreCase) &&
            e.Date.Date == request.TargetDate.Date);

        Debug.WriteLine($"[SwapRequestsPage] requesterEntry: {requesterEntry?.EmployeeName} {requesterEntry?.Date:dd.MM} {requesterEntry?.ShiftType}");
        Debug.WriteLine($"[SwapRequestsPage] targetEntry: {targetEntry?.EmployeeName} {targetEntry?.Date:dd.MM} {targetEntry?.ShiftType}");

        if (requesterEntry == null || targetEntry == null)
        {
            Debug.WriteLine($"[SwapRequestsPage] ❌ Записи не найдены!");
            return false;
        }

        // ПРАВИЛЬНАЯ ЛОГИКА ОБМЕНА:
        // Меняем имена сотрудников местами — тип смены остаётся привязан к дате
        // 
        // До обмена:
        //   3 апреля: Крылов (Д)
        //   1 апреля: Платонов (Н)
        // 
        // После обмена:
        //   3 апреля: Платонов (Д) — Платонов теперь работает в день Крылова
        //   1 апреля: Крылов (Н) — Крылов теперь работает в день Платонова

        requesterEntry.EmployeeName = request.TargetName;   // 3 апреля теперь Платонов
        targetEntry.EmployeeName = request.RequesterName;   // 1 апреля теперь Крылов

        Debug.WriteLine($"[SwapRequestsPage] После обмена:");
        Debug.WriteLine($"[SwapRequestsPage] {requesterEntry.Date:dd.MM}: {requesterEntry.EmployeeName} ({requesterEntry.ShiftType})");
        Debug.WriteLine($"[SwapRequestsPage] {targetEntry.Date:dd.MM}: {targetEntry.EmployeeName} ({targetEntry.ShiftType})");

        Debug.WriteLine($"[SwapRequestsPage] ✅ Обмен выполнен");
        return true;
    }

    /// <summary>
    /// Поменять смены между разными месяцами
    /// </summary>
    private bool SwapShiftsBetweenSchedules(MonthlySchedule schedule1, MonthlySchedule schedule2, ShiftSwapRequest request)
    {
        MonthlySchedule requesterSchedule, targetSchedule;

        if (schedule1.Year == request.RequesterDate.Year && schedule1.Month == request.RequesterDate.Month)
        {
            requesterSchedule = schedule1;
            targetSchedule = schedule2;
        }
        else
        {
            requesterSchedule = schedule2;
            targetSchedule = schedule1;
        }

        var requesterEntry = requesterSchedule.Entries.FirstOrDefault(e =>
            e.EmployeeName.Equals(request.RequesterName, StringComparison.OrdinalIgnoreCase) &&
            e.Date.Date == request.RequesterDate.Date);

        var targetEntry = targetSchedule.Entries.FirstOrDefault(e =>
            e.EmployeeName.Equals(request.TargetName, StringComparison.OrdinalIgnoreCase) &&
            e.Date.Date == request.TargetDate.Date);

        if (requesterEntry == null || targetEntry == null)
        {
            Debug.WriteLine($"[SwapRequestsPage] ❌ Записи между месяцами не найдены");
            return false;
        }

        // Меняем имена сотрудников
        requesterEntry.EmployeeName = request.TargetName;
        targetEntry.EmployeeName = request.RequesterName;

        Debug.WriteLine($"[SwapRequestsPage] ✅ Обмен между месяцами выполнен");
        return true;
    }

    private static string GetCommentFromParent(Button button)
    {
        try
        {
            if (button.Parent is Grid grid && grid.Parent is StackLayout stack)
            {
                var entry = stack.Children.OfType<Entry>().FirstOrDefault();
                return entry?.Text?.Trim() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }
}