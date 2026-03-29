using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Grafik.Services;
using System.Diagnostics;

namespace Grafik
{
    public partial class EmployeeSchedulePage : ContentPage
    {
        private const string ViewModeKey = "EmployeeViewMode";
        private string _employeeName = string.Empty;

        private List<ShiftEntry> _employeeSchedule = new();
        private List<ShiftEntry> _allSchedule = new();

        public EmployeeSchedulePage(string employeeName)
        {
            InitializeComponent();
            _employeeName = employeeName;

            // Восстанавливаем последний выбранный режим: true = календарь, false = список
            bool savedMode = Preferences.Get(ViewModeKey, false);
            ViewSwitch.IsToggled = savedMode;
            CalendarContainer.IsVisible = savedMode;
            CalendarContainer.Opacity = savedMode ? 1 : 0;
            ListContainer.IsVisible = !savedMode;
            ListContainer.Opacity = !savedMode ? 1 : 0;

            EmployeeNameLabel.Text = employeeName;

            // спрятать встроенный текст On/Off у Switch на Android
            ViewSwitch.HandlerChanged += (_, __) =>
            {
#if ANDROID
                if (ViewSwitch.Handler?.PlatformView is AndroidX.AppCompat.Widget.SwitchCompat sw)
                {
                    sw.ShowText = false;
                    sw.TextOn = string.Empty;
                    sw.TextOff = string.Empty;
                }
#elif WINDOWS
                if (ViewSwitch.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ToggleSwitch winSwitch)
                {
                    winSwitch.OnContent = "";
                    winSwitch.OffContent = "";
                }
#endif
            };

            ViewSwitch.Toggled += async (s, e) =>
            {
                Preferences.Set(ViewModeKey, e.Value);
                await AnimateSwitchAsync(e.Value);
            };

            LoadEmployeeScheduleAsync(employeeName);

            // Проверяем подключение к Firebase в silent режиме
            _ = VerifyFirebaseConnectionAsync();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // ✅ Подписываемся на событие при каждом появлении страницы
            FirebaseConnectionMonitor.Instance.SwapApplied += OnSwapApplied;

            Debug.WriteLine("[EmployeeSchedulePage] OnAppearing — подписка на SwapApplied");

            // Перезагружаем расписание при возврате на страницу
            LoadEmployeeScheduleAsync(_employeeName);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // ✅ Отписываемся от события при уходе со страницы
            FirebaseConnectionMonitor.Instance.SwapApplied -= OnSwapApplied;

            Debug.WriteLine("[EmployeeSchedulePage] OnDisappearing — отписка от SwapApplied");
        }

        /// <summary>
        /// Silent проверка подключения к Firebase без AlertDialog
        /// </summary>
        private async Task VerifyFirebaseConnectionAsync()
        {
            try
            {
                var firebaseUrl = Preferences.Get("FirebaseUrl", string.Empty);

                if (string.IsNullOrEmpty(firebaseUrl))
                {
                    Debug.WriteLine("[EmployeeSchedulePage] Firebase URL не настроен");
                    return;
                }

                Debug.WriteLine("[EmployeeSchedulePage] Проверка подключения к Firebase...");

                var firebaseService = new FirebaseService(firebaseUrl);
                var messages = await firebaseService.GetMessagesAsync();

                Debug.WriteLine($"[EmployeeSchedulePage] ✓ Подключение успешно! Сообщений: {messages.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmployeeSchedulePage] ✗ Ошибка подключения: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик события применения обмена — перезагружаем расписание
        /// </summary>
        private async void OnSwapApplied(object? sender, SwapAppliedEventArgs e)
        {
            Debug.WriteLine($"[EmployeeSchedulePage] 🔄 Получено событие обмена: {e.Swap.RequesterName} ↔️ {e.Swap.TargetName}");

            // Проверяем, относится ли обмен к текущему сотруднику
            if (e.Swap.RequesterName.Equals(_employeeName, StringComparison.OrdinalIgnoreCase) ||
                e.Swap.TargetName.Equals(_employeeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[EmployeeSchedulePage] ✅ Обмен касается {_employeeName} — перезагружаем расписание");

                // Перезагружаем расписание в UI потоке
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    LoadEmployeeScheduleAsync(_employeeName);

                    // Показываем уведомление пользователю
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

        private async Task HandleShiftSelection(ShiftEntry shift)
        {
            // Проверяем, это мой рабочий день или выходной
            bool isMyWorkDay = !string.IsNullOrWhiteSpace(shift.Shift);

            if (isMyWorkDay)
            {
                // Мой рабочий день — показываем информацию о смене
                await DisplayAlert(
                    $"{shift.Date:dd MMMM} — {shift.Shift}",
                    BuildPopupText(shift),
                    "OK"
                );
            }
            else
            {
                // Мой выходной — показываем кто работает в этот день
                var whoWorks = GetWhoWorksOnDate(shift.Date);
                
                if (string.IsNullOrEmpty(whoWorks))
                {
                    await DisplayAlert(
                        $"{shift.Date:dd MMMM, dddd}",
                        "Нет данных о сменах на этот день",
                        "OK"
                    );
                }
                else
                {
                    await DisplayAlert(
                        $"{shift.Date:dd MMMM, dddd}",
                        whoWorks,
                        "OK"
                    );
                }
            }
        }

        /// <summary>
        /// Получить информацию о том, кто работает в указанную дату
        /// </summary>
        private string GetWhoWorksOnDate(DateTime date)
        {
            var shiftsOnDate = _allSchedule
                .Where(s => s.Date.Date == date.Date && !string.IsNullOrWhiteSpace(s.Shift))
                .OrderBy(s => s.Shift)
                .ThenBy(s => s.Employees)
                .ToList();

            if (shiftsOnDate.Count == 0)
                return string.Empty;

            var lines = new List<string>();

            // Группируем по типу смены
            var groupedByShift = shiftsOnDate
                .GroupBy(s => s.Shift ?? "Без названия")
                .OrderBy(g => g.Key);

            foreach (var group in groupedByShift)
            {
                var shiftName = group.Key;
                var employees = group.ToList();

                // Разделяем на первую и вторую линию
                var firstLine = employees.Where(e => !e.IsSecondLine).Select(e => e.Employees).ToList();
                var secondLine = employees.Where(e => e.IsSecondLine).Select(e => e.Employees).ToList();

                // Получаем время работы (берём из первой записи)
                var worktime = employees.FirstOrDefault()?.Worktime ?? "";

                lines.Add($"[{shiftName}]");
                
                if (!string.IsNullOrWhiteSpace(worktime))
                    lines.Add($"  Время: {worktime}");

                if (firstLine.Count > 0)
                    lines.Add($"  {string.Join(", ", firstLine)}");

                if (secondLine.Count > 0)
                    lines.Add($"  2-я линия: {string.Join(", ", secondLine)}");

                lines.Add(""); // Пустая строка между сменами
            }

            return string.Join("\n", lines).TrimEnd();
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

        public async void LoadEmployeeScheduleAsync(string employeeName)
        {
            try
            {
                var filePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");

                if (!File.Exists(filePath))
                {
                    ScheduleListView.ItemsSource = Array.Empty<ShiftEntry>();
                    CalendarView.ItemsSource = Array.Empty<ShiftEntry>();
                    return;
                }

                var json = await File.ReadAllTextAsync(filePath);
                _allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json) ?? new();

                _employeeSchedule = _allSchedule
                    .Where(e => e.Employees == employeeName)
                    .OrderBy(e => e.Date)
                    .ToList();

                EnrichWithColleaguesInfo(_allSchedule, _employeeSchedule, employeeName);

                var today = DateTime.Today;

                foreach (var entry in _employeeSchedule)
                    entry.BorderColor = entry.Date.Date == today ? Colors.Green : Colors.Transparent;

                ScheduleListView.ItemsSource = _employeeSchedule;

                // Определяем месяц и год на основе первой записи в расписании
                DateTime monthToDisplay = today;
                if (_employeeSchedule.Count > 0)
                {
                    monthToDisplay = _employeeSchedule.First().Date;
                }

                GenerateCalendarForMonth(monthToDisplay, today);
                
                // Планируем уведомления при загрузке
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

        private void GenerateCalendarForMonth(DateTime monthDate, DateTime today)
        {
            var daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
            var firstDayOfMonth = new DateTime(monthDate.Year, monthDate.Month, 1);
            var firstDayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            // Смещение для учёта стартового дня недели (понедельник = 1, воскресенье = 0)
            var daysBeforeMonthStarts = (firstDayOfWeek + 6) % 7;

            var calendarDays = new List<ShiftEntry>();

            // Добавляем пустые ячейки для дней до начала месяца
            for (int i = 0; i < daysBeforeMonthStarts; i++)
            {
                calendarDays.Add(new ShiftEntry { Date = DateTime.MinValue, IsVisibleDay = false });
            }

            // Добавляем дни месяца
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

                // Устанавливаем текст для отображения в календаре
                day.ShiftDisplayText = GetShiftDisplayText(day, date);

                calendarDays.Add(day);
            }

            CalendarView.ItemsSource = calendarDays;
        }

        /// <summary>
        /// Получить текст для отображения в ячейке календаря
        /// </summary>
        private string GetShiftDisplayText(ShiftEntry day, DateTime date)
        {
            // Если у меня есть смена — показываем её
            if (!string.IsNullOrWhiteSpace(day.Shift))
                return day.Shift;

            // Если у меня выходной — проверяем, работает ли кто-то в этот день
            var othersWorking = _allSchedule
                .Where(s => s.Date.Date == date.Date && !string.IsNullOrWhiteSpace(s.Shift))
                .ToList();

            if (othersWorking.Count > 0)
                return "..."; // Индикатор, что можно посмотреть кто работает

            return "—"; // Никто не работает
        }

        /// <summary>
        /// Создать копию ShiftEntry для календаря (чтобы не мутировать оригинал)
        /// </summary>
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

            if (s.Contains("днев") || s.Contains("day")) return Color.FromArgb("#FF8C00");
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

        private static string BuildPopupText(ShiftEntry s)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.Worktime)) lines.Add(s.Worktime);
            if (!string.IsNullOrWhiteSpace(s.DisplayOtherEmployees) &&
                !string.Equals(s.DisplayOtherEmployees, "Нет совпадений", StringComparison.OrdinalIgnoreCase))
                lines.Add(s.DisplayOtherEmployees);
            if (!string.IsNullOrWhiteSpace(s.SecondLinePartner)) lines.Add(s.SecondLinePartner);

            return string.Join("\n", lines);
        }

        private async void GoToChat(object sender, EventArgs e)
        {
            // Передаем имя сотрудника в ChatPage
            await Navigation.PushAsync(new ChatPage(_employeeName));
        }

        private async void OnSwapClicked(object sender, EventArgs e)
{
    await Navigation.PushAsync(new ShiftSwapPage(_employeeName));
}

        /// <summary>
        /// Загрузка расписания и планирование уведомлений при инициализации страницы.
        /// Отменяет все старые уведомления перед планированием новых.
        /// </summary>
        private void LoadScheduleData()
        {
            Debug.WriteLine("[EmployeeSchedulePage] LoadScheduleData");

            try
            {
                // Получаем сохранённое напоминание (числовое значение enum)
                string reminderStr = Preferences.Get("ReminderOption", ((int)ReminderOption.FifteenMinutesBefore).ToString());
                
                // Парсим как число, затем приводим к enum
                if (int.TryParse(reminderStr, out int reminderValue) && Enum.IsDefined(typeof(ReminderOption), reminderValue))
                {
                    var reminder = (ReminderOption)reminderValue;
                    Debug.WriteLine($"[EmployeeSchedulePage] Загружено напоминание: {reminder} ({reminderValue})");

                    // Перепланируем: сначала отменяем все старые, потом создаём новые
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