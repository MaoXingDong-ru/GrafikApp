using GrafikShared.Services;
using Grafik.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace Grafik;

public partial class ShiftSwapPage : ContentPage
{
    private readonly string _employeeName;
    private readonly ShiftSwapService _swapService;

    private List<ShiftEntry> _allSchedule = [];
    private List<ShiftEntry> _myShifts = [];

    // Флаг, является ли текущий сотрудник второй линией
    private bool _isCurrentEmployeeSecondLine = false;

    private ShiftEntry? _selectedMyShift;
    private ShiftEntry? _selectedTargetShift; // Теперь это конкретная смена, а не день
    private ShiftEntry? _selectedEmployee;

    private readonly ObservableCollection<ShiftSwapRequest> _myRequests = [];

    public ShiftSwapPage(string employeeName)
    {
        InitializeComponent();

        _employeeName = employeeName;
        EmployeeLabel.Text = $"Сотрудник: {employeeName}";

        var firebaseUrl = Preferences.Get("FirebaseUrl",
            "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");
        _swapService = new ShiftSwapService(firebaseUrl);

        MyRequestsCollection.ItemsSource = _myRequests;

        LoadDataAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadMyRequestsAsync();
    }

    private async void LoadDataAsync()
    {
        try
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");

            if (!File.Exists(filePath))
            {
                await DisplayAlert("Ошибка", "Файл расписания не найден", "OK");
                return;
            }

            var json = await File.ReadAllTextAsync(filePath);

            Debug.WriteLine($"[ShiftSwapPage] JSON длина: {json.Length}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            _allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json, options) ?? [];

            Debug.WriteLine($"[ShiftSwapPage] Загружено записей: {_allSchedule.Count}");

            // Определяем, к какой линии относится текущий сотрудник
            _isCurrentEmployeeSecondLine = _allSchedule
                .Any(s => s.Employees.Equals(_employeeName, StringComparison.OrdinalIgnoreCase)
                       && s.IsSecondLine);

            Debug.WriteLine($"[ShiftSwapPage] Сотрудник '{_employeeName}' — {(_isCurrentEmployeeSecondLine ? "вторая линия" : "первая линия")}");

            _myShifts = _allSchedule
                .Where(s => s.Employees.Equals(_employeeName, StringComparison.OrdinalIgnoreCase)
                         && s.Date.Date >= DateTime.Today
                         && !string.IsNullOrWhiteSpace(s.Shift))
                .OrderBy(s => s.Date)
                .ThenBy(s => IsNightShift(s.Shift)) // Сначала дневные, потом ночные
                .ToList();

            Debug.WriteLine($"[ShiftSwapPage] Мои смены: {_myShifts.Count}");

            foreach (var shift in _myShifts)
            {
                shift.TileColor = GetTileColorForShift(shift.Shift);
                shift.BorderColor = Colors.Transparent;
            }

            MyShiftsCollection.ItemsSource = _myShifts;

            await LoadMyRequestsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShiftSwapPage] Ошибка загрузки: {ex.Message}");
            Debug.WriteLine($"[ShiftSwapPage] Stack: {ex.StackTrace}");
            await DisplayAlert("Ошибка", $"Не удалось загрузить данные: {ex.Message}", "OK");
        }
    }

    private async Task LoadMyRequestsAsync()
    {
        try
        {
            var requests = await _swapService.GetRequestsForEmployeeAsync(_employeeName);

            _myRequests.Clear();
            foreach (var req in requests.OrderByDescending(r => r.CreatedAt))
            {
                _myRequests.Add(req);
            }

            Debug.WriteLine($"[ShiftSwapPage] Загружено {_myRequests.Count} запросов");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShiftSwapPage] Ошибка загрузки запросов: {ex.Message}");
        }
    }

    private void OnMyShiftSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ShiftEntry selected)
            return;

        _selectedMyShift = selected;

        foreach (var shift in _myShifts)
            shift.BorderColor = shift == selected ? Colors.Green : Colors.Transparent;

        MyShiftsCollection.ItemsSource = null;
        MyShiftsCollection.ItemsSource = _myShifts;

        SelectedMyDayLabel.Text = $"Выбрано: {selected.Date:dd.MM} ({selected.Shift})";

        ShowTargetShifts();
    }

    private void ShowTargetShifts()
    {
        if (_selectedMyShift == null)
            return;

        // Получаем ВСЕ смены других сотрудников (каждая смена отдельно)
        // Группируем по дате + тип смены, чтобы показать уникальные варианты
        var targetShifts = _allSchedule
            .Where(s => !s.Employees.Equals(_employeeName, StringComparison.OrdinalIgnoreCase)
                     && s.Date.Date >= DateTime.Today
                     && !string.IsNullOrWhiteSpace(s.Shift)
                     && s.IsSecondLine == _isCurrentEmployeeSecondLine)
            .GroupBy(s => new { s.Date.Date, ShiftType = GetShiftType(s.Shift) }) // Группируем по дате + тип смены
            .Select(g => g.First()) // Берём первого представителя каждой группы
            .OrderBy(s => s.Date)
            .ThenBy(s => IsNightShift(s.Shift)) // Сначала дневные, потом ночные
            .ToList();

        Debug.WriteLine($"[ShiftSwapPage] Доступных смен для обмена: {targetShifts.Count}");

        foreach (var shift in targetShifts)
        {
            shift.TileColor = GetTileColorForShift(shift.Shift);
            shift.BorderColor = Colors.Transparent;
        }

        TargetDaysCollection.ItemsSource = targetShifts;
        Step2Frame.IsVisible = true;

        Step3Frame.IsVisible = false;
        SummaryFrame.IsVisible = false;
        _selectedTargetShift = null;
        _selectedEmployee = null;
        SelectedTargetDayLabel.Text = "Выбрано: —";
    }

    private void OnTargetDaySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ShiftEntry selected)
            return;

        _selectedTargetShift = selected;
        SelectedTargetDayLabel.Text = $"Выбрано: {selected.Date:dd.MM} ({selected.Shift})";

        ShowEmployeesForShift(selected.Date, selected.Shift);
    }

    private void ShowEmployeesForShift(DateTime date, string? shift)
    {
        // Фильтруем сотрудников по дате И типу смены
        var shiftType = GetShiftType(shift);

        var employees = _allSchedule
            .Where(s => s.Date.Date == date.Date
                     && !s.Employees.Equals(_employeeName, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(s.Shift)
                     && s.IsSecondLine == _isCurrentEmployeeSecondLine
                     && GetShiftType(s.Shift) == shiftType) // Тот же тип смены
            .ToList();

        Debug.WriteLine($"[ShiftSwapPage] Сотрудников на {date:dd.MM} ({shift}): {employees.Count}");

        EmployeesCollection.ItemsSource = employees;
        EmployeesWorkingLabel.Text = $"Сотрудники на {date:dd.MM} ({shift}):";
        Step3Frame.IsVisible = true;

        SummaryFrame.IsVisible = false;
        _selectedEmployee = null;
        SelectedEmployeeLabel.Text = "Выбран: —";
    }

    private void OnEmployeeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ShiftEntry selected)
            return;

        _selectedEmployee = selected;
        SelectedEmployeeLabel.Text = $"Выбран: {selected.Employees}";

        ShowSummary();
    }

    private void ShowSummary()
    {
        if (_selectedMyShift == null || _selectedTargetShift == null || _selectedEmployee == null)
            return;

        SummaryLabel.Text =
            $"Вы ({_employeeName}) отдаёте:\n" +
            $"   📅 {_selectedMyShift.Date:dd.MM.yyyy} — {_selectedMyShift.Shift}\n\n" +
            $"Взамен получаете от {_selectedEmployee.Employees}:\n" +
            $"   📅 {_selectedEmployee.Date:dd.MM.yyyy} — {_selectedEmployee.Shift}";

        SummaryFrame.IsVisible = true;
    }

    private async void OnSendRequestClicked(object sender, EventArgs e)
    {
        if (_selectedMyShift == null || _selectedEmployee == null)
        {
            await DisplayAlert("Ошибка", "Выберите все параметры обмена", "OK");
            return;
        }

        try
        {
            SendRequestButton.IsEnabled = false;

            var request = new ShiftSwapRequest
            {
                RequesterName = _employeeName,
                RequesterDate = _selectedMyShift.Date,
                RequesterShift = _selectedMyShift.Shift ?? "",
                TargetName = _selectedEmployee.Employees,
                TargetDate = _selectedEmployee.Date,
                TargetShift = _selectedEmployee.Shift ?? "",
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            var success = await _swapService.CreateSwapRequestAsync(request);

            if (success)
            {
                await DisplayAlert("✅ Успех", "Запрос на обмен отправлен!", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("❌ Ошибка", "Не удалось отправить запрос", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShiftSwapPage] Ошибка отправки: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось отправить запрос: {ex.Message}", "OK");
        }
        finally
        {
            SendRequestButton.IsEnabled = true;
        }
    }

    private async void OnCancelRequestClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ShiftSwapRequest request)
            return;

        bool confirm = await DisplayAlert(
            "Подтверждение",
            "Вы уверены, что хотите отменить запрос?",
            "Да", "Нет");

        if (!confirm)
            return;

        try
        {
            var success = await _swapService.DeleteRequestAsync(request.FirebaseKey);

            if (success)
            {
                await DisplayAlert("✅ Успех", "Запрос отменён", "OK");
                await LoadMyRequestsAsync();
            }
            else
            {
                await DisplayAlert("❌ Ошибка", "Не удалось отменить запрос", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShiftSwapPage] Ошибка отмены: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось отменить запрос: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Определяет тип смены: "day", "night" или "other"
    /// </summary>
    private static string GetShiftType(string? shift)
    {
        if (string.IsNullOrWhiteSpace(shift))
            return "other";

        var s = shift.ToLowerInvariant();

        if (s.Contains("днев") || s.Contains("day"))
            return "day";

        if (s.Contains("ноч") || s.Contains("night"))
            return "night";

        return "other";
    }

    /// <summary>
    /// Проверяет, является ли смена ночной (для сортировки)
    /// </summary>
    private static bool IsNightShift(string? shift)
    {
        if (string.IsNullOrWhiteSpace(shift))
            return false;

        var s = shift.ToLowerInvariant();
        return s.Contains("ноч") || s.Contains("night");
    }

    private static Color GetTileColorForShift(string? shift)
    {
        if (string.IsNullOrWhiteSpace(shift))
            return Colors.Gray;

        var s = shift.ToLowerInvariant();

        if (s.Contains("днев") || s.Contains("day"))
            return Color.FromArgb("#FF8C00"); // Оранжевый для дневной

        if (s.Contains("ноч") || s.Contains("night"))
            return Color.FromArgb("#00008B"); // Тёмно-синий для ночной

        return Colors.Gray;
    }
}