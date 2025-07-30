using System.Text.Json;

namespace Grafik
{
    public partial class EmployeeSchedulePage : ContentPage
    {
        public EmployeeSchedulePage(string employeeName)
        {
            InitializeComponent();

            // Отображаем имя сотрудника
            EmployeeNameLabel.Text = employeeName;

            LoadEmployeeScheduleAsync(employeeName);
        }

        // Асинхронная загрузка расписания
        public async void LoadEmployeeScheduleAsync(string employeeName)
        {
            try
            {
                var filePath = Path.Combine(FileSystem.AppDataDirectory, "schedule.json");

                if (!File.Exists(filePath))
                {
                    ScheduleListView.ItemsSource = new List<ShiftEntry>();
                    return;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json) ?? new();

                // Получаем только смены выбранного сотрудника
                var employeeSchedule = allSchedule
                    .Where(e => e.Employees == employeeName)
                    .ToList();

                foreach (var entry in employeeSchedule)
                {
                    // Список коллег с той же сменой
                    var sameShiftEntries = allSchedule
                        .Where(e =>
                            e.Date == entry.Date &&
                            e.Shift == entry.Shift &&
                            e.Employees != employeeName)
                        .Select(e => e.Employees)
                        .Distinct()
                        .ToList();

                    entry.DisplayOtherEmployees = sameShiftEntries.Count > 0
                        ? "Коллеги: " + string.Join(", ", sameShiftEntries)
                        : "Нет совпадений";

                    // 🆕 если сотрудник из первой линии — ищем кого-то из второй на той же смене
                    if (!entry.IsSecondLine)
                    {
                        var secondLinePerson = allSchedule
                            .FirstOrDefault(e =>
                                e.Date == entry.Date &&
                                e.Shift == entry.Shift &&
                                e.IsSecondLine &&
                                e.Employees != employeeName);

                        if (secondLinePerson != null)
                        {
                            entry.SecondLinePartner = "Вторая линия: " + secondLinePerson.Employees;
                        }
                    }
                }

                ScheduleListView.ItemsSource = employeeSchedule;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке расписания: {ex.Message}");
                await DisplayAlert("Ошибка", "Произошла ошибка при загрузке данных.", "OK");
            }
        }
    }

}
