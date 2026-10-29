using Grafik.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Grafik
{
    public partial class EmployeeSchedulePage : ContentPage
    {
        private const string ViewModeKey = "EmployeeViewMode";
        private const string SelectedMonthKey = "SelectedScheduleMonth";

        private string _employeeName = string.Empty;

        private List<ShiftEntry> _employeeSchedule = new();
        private List<ShiftEntry> _allSchedule = new();

        // Слоты: до 3 месяцев по возрастанию
        private List<ScheduleMonth> _slots = new();
        private ScheduleMonth? _selectedMonth;

        // Текущий режим: true = календарь (таблица), false = список
        private bool _isCalendarMode;

        public EmployeeSchedulePage(string employeeName, ScheduleMonth? initialMonth = null)
        {
            InitializeComponent();
            _employeeName = employeeName;

            if (initialMonth != null)
            {
                _selectedMonth = initialMonth;
                Preferences.Set(SelectedMonthKey, initialMonth.Value.Key);
            }

            // Восстанавливаем последний выбранный режим: true = календарь, false = список
            bool savedMode = Preferences.Get(ViewModeKey, false);
            _isCalendarMode = savedMode;
            CalendarContainer.IsVisible = savedMode;
            CalendarContainer.Opacity = savedMode ? 1 : 0;
            ListContainer.IsVisible = !savedMode;
            ListContainer.Opacity = !savedMode ? 1 : 0;
            UpdateViewModeButton();

            EmployeeNameLabel.Text = employeeName;

            // Расписание загружается из локальных файлов — интернет не нужен
            LoadEmployeeScheduleAsync(employeeName);
        }

        // Кнопка показывает режим, в который переключит
        private void UpdateViewModeButton()
        {
            ViewModeButton.Text = _isCalendarMode ? "Список" : "Таблица";
        }

        private async void OnViewModeClicked(object sender, EventArgs e)
        {
            _isCalendarMode = !_isCalendarMode;
            Preferences.Set(ViewModeKey, _isCalendarMode);
            UpdateViewModeButton();
            await AnimateSwitchAsync(_isCalendarMode);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Подписываемся на событие обмена сменами
            FirebaseConnectionMonitor.Instance.SwapApplied += OnSwapApplied;

            // Подписываемся на изменение статуса соединения — обновляем кнопку чата
            FirebaseConnectionMonitor.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Устанавливаем текущее состояние кнопки сразу
            ChatButton.IsVisible = FirebaseConnectionMonitor.Instance.IsConnected;

            Debug.WriteLine("[EmployeeSchedulePage] OnAppearing");

            // Перезагружаем расписание при возврате на страницу (из локальных файлов)
            LoadEmployeeScheduleAsync(_employeeName);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            FirebaseConnectionMonitor.Instance.SwapApplied -= OnSwapApplied;
            FirebaseConnectionMonitor.Instance.ConnectionStatusChanged -= OnConnectionStatusChanged;

            Debug.WriteLine("[EmployeeSchedulePage] OnDisappearing");
        }

        private void OnConnectionStatusChanged(object? sender, bool isConnected)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ChatButton.IsVisible = isConnected;
                Debug.WriteLine($"[EmployeeSchedulePage] Кнопка чата: {(isConnected ? "видима ✓" : "скрыта ✗")}");
            });
        }

        private async void OnSwapApplied(object? sender, SwapAppliedEventArgs e)
        {
            Debug.WriteLine($"[EmployeeSchedulePage] 🔄 Получено событие обмена: {e.Swap.RequesterName} ↔️ {e.Swap.TargetName}");

            if (e.Swap.RequesterName.Equals(_employeeName, StringComparison.OrdinalIgnoreCase) ||
                e.Swap.TargetName.Equals(_employeeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[EmployeeSchedulePage] ✅ Обмен касается {_employeeName} — перезагружаем расписание");

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    LoadEmployeeScheduleAsync(_employeeName);

                    await DisplayAlert(
                        "🔄 Расписание обновлено",
                        $"Обмен сменами одобрен!\n\n{e.Swap.RequesterName} ({e.Swap.RequesterDate:dd.MM}) ↔️ {e.Swap.TargetName} ({e.Swap.TargetDate:dd.MM})",
                        "OK");
                });
            }
        }

        private async void OnCalendarItemTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is ShiftEntry shift)
            {
                await HandleShiftSelection(shift);
            }
        }

        private async void OnScheduleListItemTapped(object sender, EventArgs e)
        {
            if (sender is Frame frame && frame.BindingContext is ShiftEntry shift)
            {
                await HandleShiftSelection(shift);
            }
        }

        private async Task HandleShiftSelection(ShiftEntry shift)
        {
            bool isMyWorkDay = !string.IsNullOrWhiteSpace(shift.Shift);
            var whoWorks = GetWhoWorksOnDate(shift.Date, highlightEmployee: _employeeName);

            if (string.IsNullOrEmpty(whoWorks))
            {
                await DisplayAlert(
                    $"{shift.Date:dd MMMM, dddd}",
                    "Нет данных о сменах на этот день",
                    "OK"
                );
                return;
            }

            string title = isMyWorkDay
                ? $"{shift.Date:dd MMMM, dddd} (Ваша смена: {shift.Shift})"
                : $"{shift.Date:dd MMMM, dddd}";

            await DisplayAlert(title, whoWorks, "OK");
        }

        private string GetWhoWorksOnDate(DateTime date, string? highlightEmployee = null)
        {
            var shiftsOnDate = _allSchedule
                .Where(s => s.Date.Date == date.Date && !string.IsNullOrWhiteSpace(s.Shift))
                .OrderBy(s => s.Shift)
                .ThenBy(s => s.Employees)
                .ToList();

            if (shiftsOnDate.Count == 0)
                return string.Empty;

            var lines = new List<string>();

            var groupedByShift = shiftsOnDate
                .GroupBy(s => s.Shift ?? "Без названия")
                .OrderBy(g => g.Key);

            foreach (var group in groupedByShift)
            {
                var shiftName = group.Key;
                var employees = group.ToList();

                bool isMyShift = !string.IsNullOrEmpty(highlightEmployee) &&
                                 employees.Any(e => string.Equals(e.Employees, highlightEmployee, StringComparison.OrdinalIgnoreCase));

                var firstLine = employees
                    .Where(e => !e.IsSecondLine)
                    .Select(e => FormatEmployeeName(e.Employees, highlightEmployee))
                    .ToList();

                var secondLine = employees
                    .Where(e => e.IsSecondLine)
                    .Select(e => FormatEmployeeName(e.Employees, highlightEmployee))
                    .ToList();

                var worktime = employees.FirstOrDefault()?.Worktime ?? "";

                string header = isMyShift ? $"★ [{shiftName}] (Ваша смена)" : $"[{shiftName}]";
                lines.Add(header);

                if (!string.IsNullOrWhiteSpace(worktime))
                    lines.Add($"  Время: {worktime}");

                if (firstLine.Count > 0)
                    lines.Add($"  {string.Join(", ", firstLine)}");

                if (secondLine.Count > 0)
                    lines.Add($"  2-я линия: {string.Join(", ", secondLine)}");

                lines.Add("");
            }

            return string.Join("\n", lines).TrimEnd();
        }

        private static string FormatEmployeeName(string name, string? highlightEmployee)
        {
            if (!string.IsNullOrEmpty(highlightEmployee) &&
                string.Equals(name, highlightEmployee, StringComparison.OrdinalIgnoreCase))
            {
                return $"➤ {name} (Вы)";
            }

            return name;
        }

        private async System.Threading.Tasks.Task AnimateSwitchAsync(bool toCalendar)
        {
            if (toCalendar)
            {
                CalendarContainer.IsVisible = true;
                await ListContainer.FadeTo(0, 250);
                ListContainer.IsVisible = false;
                await CalendarContainer.FadeTo(1, 250);
            }
            else
            {
                ListContainer.IsVisible = true;
                await CalendarContainer.FadeTo(0, 250);
                CalendarContainer.IsVisible = false;
                await ListContainer.FadeTo(1, 250);
            }
        }

        // ───────────── Загрузка и слоты ─────────────

        public async void LoadEmployeeScheduleAsync(string employeeName)
        {
            try
            {
                _allSchedule = await Task.Run(ScheduleSlotService.LoadAll);
                _slots = await Task.Run(ScheduleSlotService.GetSlots);

                if (_slots.Count == 0)
                {
                    _selectedMonth = null;
                    _employeeSchedule = new();
                    ScheduleListView.ItemsSource = Array.Empty<ShiftEntry>();
                    CalendarView.ItemsSource = Array.Empty<ShiftEntry>();
                    UpdateMonthPickerButton();
                    UpdateInsights();
                    return;
                }

                // Если выбранный месяц исчез (слот вытеснен) — выбираем новый
                if (_selectedMonth is not ScheduleMonth current || !_slots.Contains(current))
                    _selectedMonth = PickDefaultMonth();

                ApplySelectedMonth();

                // _allSchedule содержит все слоты — уведомления планируются на все месяцы
                LoadScheduleData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке расписания: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", "Произошла ошибка при загрузке данных.", "OK");
                });
            }
        }

        private ScheduleMonth PickDefaultMonth()
        {
            // 1. Последний выбранный месяц
            var savedKey = Preferences.Get(SelectedMonthKey, string.Empty);
            var saved = _slots.Where(m => m.Key == savedKey).Cast<ScheduleMonth?>().FirstOrDefault();
            if (saved != null)
                return saved.Value;

            // 2. Текущий месяц, иначе ближайший прошедший, иначе самый ранний
            var now = new ScheduleMonth(DateTime.Today.Year, DateTime.Today.Month);
            if (_slots.Contains(now))
                return now;

            return _slots.Where(m => m.CompareTo(now) <= 0).DefaultIfEmpty(_slots[0]).Last();
        }

        private void ApplySelectedMonth()
        {
            if (_selectedMonth is not ScheduleMonth month)
                return;

            var today = DateTime.Today;

            _employeeSchedule = _allSchedule
                .Where(e => e.Employees == _employeeName && month.Contains(e.Date))
                .OrderBy(e => e.Date)
                .ToList();

            EnrichWithColleaguesInfo(_allSchedule, _employeeSchedule, _employeeName);

            foreach (var entry in _employeeSchedule)
                entry.BorderColor = entry.Date.Date == today ? Colors.Green : Colors.Transparent;

            ScheduleListView.ItemsSource = _employeeSchedule;
            GenerateCalendarForMonth(new DateTime(month.Year, month.Month, 1), today);

            UpdateMonthPickerButton();
            UpdateInsights();
        }

        // ───────────── Выбор месяца (кнопка-«пикер») ─────────────

        private void UpdateMonthPickerButton()
        {
            if (_slots.Count == 0 || _selectedMonth is not ScheduleMonth month)
            {
                MonthPickerButton.IsVisible = false;
                return;
            }

            MonthPickerButton.Text = $"{month.Title} ▾";
            MonthPickerButton.IsVisible = true;
        }

        private async void OnMonthPickerClicked(object sender, EventArgs e)
        {
            if (_slots.Count == 0)
                return;

            // Заголовки слотов в порядке возрастания; выбранный отмечаем галочкой
            var titles = _slots
                .Select(m => m == _selectedMonth ? $"✓ {m.Title}" : m.Title)
                .ToArray();

            var choice = await DisplayActionSheet("Выберите месяц", "Отмена", null, titles);

            if (string.IsNullOrEmpty(choice) || choice == "Отмена")
                return;

            int index = Array.IndexOf(titles, choice);
            if (index < 0 || _slots[index] == _selectedMonth)
                return;

            _selectedMonth = _slots[index];
            Preferences.Set(SelectedMonthKey, _selectedMonth.Value.Key);
            ApplySelectedMonth();
        }

        // ───────────── Карточки: ближайшая смена, статистика, «Сегодня на смене» ─────────────

        private void UpdateInsights()
        {
            InfoCard.IsVisible = _slots.Count > 0;

            if (_slots.Count == 0)
            {
                TodayCard.IsVisible = false;
                return;
            }

            var now = DateTime.Now;

            // Ближайшая смена (по всем слотам)
            var next = ScheduleInsights.GetNextShift(_allSchedule, _employeeName, now);

            if (next == null)
            {
                NextShiftTitleLabel.Text = "Ближайшая смена";
                NextShiftTextLabel.Text = "Предстоящих смен нет";
                NextShiftCountdownLabel.Text = string.Empty;
            }
            else
            {
                NextShiftTitleLabel.Text = next.IsActive ? "Сейчас на смене" : "Ближайшая смена";
                NextShiftTextLabel.Text =
                    $"{ScheduleInsights.DayLabel(next.Start, now)} · {next.Entry.Shift} · {next.Entry.Worktime}";
                NextShiftCountdownLabel.Text = ScheduleInsights.Countdown(next, now);
            }

            // Статистика выбранного месяца
            if (_selectedMonth is ScheduleMonth month)
                MonthStatsLabel.Text = ScheduleInsights.FormatStats(
                    ScheduleInsights.GetMonthStats(_employeeSchedule, month));

            // Сегодня на смене
            var today = ScheduleInsights.DescribeWorkingOn(_allSchedule, now, _employeeName);
            TodayCard.IsVisible = !string.IsNullOrEmpty(today);
            SetTodayText(today);
        }

        // Заголовки смен — жирным, строки с именами — без начальных пробелов и с одинаковым выравниванием
        private void SetTodayText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TodayLabel.FormattedText = null;
                TodayLabel.Text = string.Empty;
                return;
            }

            var formatted = new FormattedString();
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                bool isHeader = line.Length > 0 && !char.IsWhiteSpace(line[0]);

                var span = new Span
                {
                    Text = line.Trim() + (i < lines.Length - 1 ? "\n" : string.Empty),
                    FontAttributes = isHeader ? FontAttributes.Bold : FontAttributes.None,
                    FontSize = isHeader ? 14 : 13
                };

                if (!isHeader)
                    span.TextColor = Colors.Gray;

                formatted.Spans.Add(span);
            }

            TodayLabel.FormattedText = formatted;
        }

        // ───────────── Календарь ─────────────

        private void GenerateCalendarForMonth(DateTime monthDate, DateTime today)
        {
            var daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
            var firstDayOfMonth = new DateTime(monthDate.Year, monthDate.Month, 1);
            var firstDayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            var daysBeforeMonthStarts = (firstDayOfWeek + 6) % 7;

            var calendarDays = new List<ShiftEntry>();

            for (int i = 0; i < daysBeforeMonthStarts; i++)
            {
                calendarDays.Add(new ShiftEntry { Date = DateTime.MinValue, IsVisibleDay = false });
            }

            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(monthDate.Year, monthDate.Month, d);
                var shift = _employeeSchedule.FirstOrDefault(s => s.Date.Date == date);

                var day = shift != null
                    ? CloneShiftEntry(shift)
                    : new ShiftEntry { Date = date, IsVisibleDay = true };

                day.TileColor = GetTileColorForShift(day.Shift);
                day.BorderColor = date == today ? Colors.Green : Colors.Transparent;
                day.IsVisibleDay = true;
                day.ShiftDisplayText = GetShiftDisplayText(day, date);

                calendarDays.Add(day);
            }

            CalendarView.ItemsSource = calendarDays;
        }

        private string GetShiftDisplayText(ShiftEntry day, DateTime date)
        {
            if (!string.IsNullOrWhiteSpace(day.Shift))
                return day.Shift;

            var othersWorking = _allSchedule
                .Where(s => s.Date.Date == date.Date && !string.IsNullOrWhiteSpace(s.Shift))
                .ToList();

            if (othersWorking.Count > 0)
                return "...";

            return "—";
        }

        private static ShiftEntry CloneShiftEntry(ShiftEntry original)
        {
            return new ShiftEntry
            {
                Date = original.Date,
                Shift = original.Shift,
                Worktime = original.Worktime,
                Employees = original.Employees,
                IsSecondLine = original.IsSecondLine,
                DisplayOtherEmployees = original.DisplayOtherEmployees,
                SecondLinePartner = original.SecondLinePartner,
                IsVisibleDay = original.IsVisibleDay
            };
        }

        private static Color GetTileColorForShift(string? shift)
        {
            if (string.IsNullOrWhiteSpace(shift)) return Colors.Transparent;
            var s = shift.ToLowerInvariant();

            if (s.Contains("дневн") || s.Contains("day")) return Color.FromArgb("#FF8C00");
            if (s.Contains("ноч") || s.Contains("night")) return Color.FromArgb("#00008B");
            if (s.Contains("выходной") || s.Contains("вых")) return Color.FromArgb("#E0E0E0");
            return Colors.Transparent;
        }

        private static void EnrichWithColleaguesInfo(List<ShiftEntry> all, List<ShiftEntry> emp, string employeeName)
        {
            var secondLineEmployees = all.Where(e => e.IsSecondLine)
                                         .Select(e => e.Employees)
                                         .Distinct()
                                         .ToHashSet();

            foreach (var entry in emp)
            {
                var sameShift = all.Where(e => e.Date.Date == entry.Date.Date
                                            && string.Equals(e.Shift, entry.Shift, StringComparison.OrdinalIgnoreCase)
                                            && e.Employees != employeeName
                                            && !e.IsSecondLine
                                            && !secondLineEmployees.Contains(e.Employees))
                                   .Select(e => e.Employees)
                                   .Distinct()
                                   .ToList();

                entry.DisplayOtherEmployees = sameShift.Count > 0
                    ? "Коллеги: " + string.Join(", ", sameShift)
                    : "Нет совпадений";

                if (!entry.IsSecondLine)
                {
                    var secondLinePerson = all.FirstOrDefault(e => e.Date.Date == entry.Date.Date
                                                                && string.Equals(e.Shift, entry.Shift, StringComparison.OrdinalIgnoreCase)
                                                                && e.IsSecondLine
                                                                && e.Employees != employeeName);
                    entry.SecondLinePartner = secondLinePerson != null
                        ? "Вторая линия: " + secondLinePerson.Employees
                        : null;
                }
            }
        }

        private async void GoToChat(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ChatPage(_employeeName));
        }

        private async void OnSwapClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ShiftSwapPage(_employeeName));
        }

        // ───────────── Уведомления ─────────────

        private void LoadScheduleData()
        {
            Debug.WriteLine("[EmployeeSchedulePage] LoadScheduleData");

            try
            {
                string reminderStr = Preferences.Get("ReminderOption", ((int)ReminderOption.FifteenMinutesBefore).ToString());

                if (int.TryParse(reminderStr, out int reminderValue) && Enum.IsDefined(typeof(ReminderOption), reminderValue))
                {
                    var reminder = (ReminderOption)reminderValue;
                    Debug.WriteLine($"[EmployeeSchedulePage] Загружено напоминание: {reminder} ({reminderValue})");

                    NotificationService.RescheduleAllForEmployee(_employeeName, _allSchedule, reminder);

                    Debug.WriteLine($"[EmployeeSchedulePage] ✅ Уведомления перепланированы для {_employeeName}");
                }
                else
                {
                    Debug.WriteLine($"[EmployeeSchedulePage] ⚠️ Не удалось распарсить напоминание: {reminderStr}, используем 15 минут");
                    NotificationService.RescheduleAllForEmployee(_employeeName, _allSchedule, ReminderOption.FifteenMinutesBefore);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmployeeSchedulePage] ❌ Ошибка: {ex.Message}");
            }
        }
    }
}