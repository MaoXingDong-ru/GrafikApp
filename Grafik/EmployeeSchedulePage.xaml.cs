using System.Text.Json;

namespace Grafik
{
    public partial class EmployeeSchedulePage : ContentPage
    {
        public EmployeeSchedulePage(string employeeName)
        {
            InitializeComponent();

            // ���������� ��� ����������
            EmployeeNameLabel.Text = employeeName;

            LoadEmployeeScheduleAsync(employeeName);
        }

        // ����������� �������� ����������
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

                var json = await File.ReadAllTextAsync(filePath); // ���������� ��������� JSON
                var allSchedule = JsonSerializer.Deserialize<List<ShiftEntry>>(json) ?? new();

                // �������� ������ ����� ���������� ����������
                var employeeSchedule = allSchedule
                    .Where(e => e.Employees == employeeName)
                    .ToList();

                // ��� ������ ����� � ���� ����������� �����������
                foreach (var entry in employeeSchedule)
                {
                    var sameShiftEntries = allSchedule
                        .Where(e =>
                            e.Date == entry.Date &&
                            e.Shift == entry.Shift && // ��������� � ��� �����
                            e.Employees != employeeName)
                        .Select(e => e.Employees)
                        .Distinct()
                        .ToList();

                    entry.DisplayOtherEmployees = sameShiftEntries.Count > 0
                        ? "����������: " + string.Join(", ", sameShiftEntries)
                        : "��� ����������";
                }

                // ���������� ����������
                ScheduleListView.ItemsSource = employeeSchedule;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"������ ��� �������� ����������: {ex.Message}");
                await DisplayAlert("������", "��������� ������ ��� �������� ������.", "OK");
            }
        }
    }

}
