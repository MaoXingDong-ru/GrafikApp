﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Grafik
{
    public partial class EmployeeSchedulePage : ContentPage
    {
        private const string ViewModeKey = "EmployeeViewMode";

        private List<ShiftEntry> _employeeSchedule = new();

        public EmployeeSchedulePage(string employeeName)
        {
            InitializeComponent();
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
        winSwitch.OnContent = "";    // Убираем "On"
        winSwitch.OffContent = "";   // Убираем "Off"
    }
#endif
            };

            ViewSwitch.Toggled += async (s, e) =>
            {
                // Сохраняем режим: true = календарь, false = список
                Preferences.Set(ViewModeKey, e.Value);
                await AnimateSwitchAsync(e.Value);
            };


            CalendarView.SelectionChanged += async (s, e) =>
            {
                if (e.CurrentSelection.FirstOrDefault() is ShiftEntry shift &&
                    !string.IsNullOrWhiteSpace(shift.Shift))
                {
                    await DisplayAlert(
                        $"{shift.Date:dd MMMM} — {shift.Shift}",
                        BuildPopupText(shift),
                        "OK"
                    );
                }
            };

            LoadEmployeeScheduleAsync(employeeName);
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
                var allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json) ?? new();

                var employeeSchedule = allSchedule
                    .Where(e => e.Employees == employeeName)
                    .OrderBy(e => e.Date)
                    .ToList();

                EnrichWithColleaguesInfo(allSchedule, employeeSchedule, employeeName);

                var today = DateTime.Today;

                foreach (var e in employeeSchedule)
                    e.BorderColor = e.Date.Date == today ? Colors.Green : Colors.Transparent;

                var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                var calendarDays = new List<ShiftEntry>();

                for (int d = 1; d <= daysInMonth; d++)
                {
                    var date = new DateTime(today.Year, today.Month, d);
                    var shift = employeeSchedule.FirstOrDefault(s => s.Date.Date == date);

                    var day = shift ?? new ShiftEntry { Date = date };
                    day.TileColor = GetTileColorForShift(day.Shift);
                    day.BorderColor = date == today ? Colors.Green : Colors.Transparent;

                    calendarDays.Add(day);
                }

                ScheduleListView.ItemsSource = employeeSchedule;
                CalendarView.ItemsSource = calendarDays;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке расписания: {ex.Message}");
                await DisplayAlert("Ошибка", "Произошла ошибка при загрузке данных.", "OK");
            }
        }

        private static Color GetTileColorForShift(string? shift)
        {
            if (string.IsNullOrWhiteSpace(shift)) return Colors.Transparent;
            var s = shift.ToLowerInvariant();

            if (s.Contains("днев") || s.Contains("day")) return Color.FromArgb("#FF8C00");
            if (s.Contains("ноч") || s.Contains("night")) return Color.FromArgb("#00008B");
            if (s.Contains("выход")) return Color.FromArgb("#E0E0E0");
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
    }
}
