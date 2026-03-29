using GrafikAdmin.Services;

namespace GrafikAdmin;

public partial class EmployeesPage : ContentPage
{
    private readonly EmployeeStorageService _employeeService = new();
    private EmployeeList _employees = new();

    public EmployeesPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadEmployeesAsync();
    }

    private async Task LoadEmployeesAsync()
    {
        _employees = await _employeeService.LoadAsync();

        FirstLineList.ItemsSource = null;
        SecondLineList.ItemsSource = null;

        FirstLineList.ItemsSource = _employees.FirstLine;
        SecondLineList.ItemsSource = _employees.SecondLine;

        CountLabel.Text = $"Всего: {_employees.TotalCount} (1 линия: {_employees.FirstLine.Count}, 2 линия: {_employees.SecondLine.Count})";
    }

    private async void OnAddEmployeeClicked(object sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync(
            "Добавить сотрудника",
            "Введите ФИО сотрудника:",
            placeholder: "Иванов Иван Иванович");

        if (string.IsNullOrWhiteSpace(name))
            return;

        name = name.Trim();

        // Проверяем дубликат
        if (_employees.All.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            await DisplayAlert("Ошибка", "Сотрудник с таким именем уже существует", "OK");
            return;
        }

        // Выбор линии
        string line = await DisplayActionSheet(
            "Выберите линию",
            "Отмена",
            null,
            "🔵 Первая линия",
            "🟢 Вторая линия");

        if (line == "Отмена" || line == null)
            return;

        bool isSecondLine = line.Contains("Вторая");

        bool success = await _employeeService.AddEmployeeAsync(name, isSecondLine);

        if (success)
        {
            await LoadEmployeesAsync();
            await DisplayAlert("Успех", $"Сотрудник \"{name}\" добавлен", "OK");
        }
        else
        {
            await DisplayAlert("Ошибка", "Не удалось добавить сотрудника", "OK");
        }
    }

    private async void OnDeleteEmployee(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.BindingContext is string employeeName)
        {
            bool confirm = await DisplayAlert(
                "Подтверждение",
                $"Удалить сотрудника \"{employeeName}\"?",
                "Да, удалить",
                "Отмена");

            if (!confirm)
                return;

            bool success = await _employeeService.RemoveEmployeeAsync(employeeName);

            if (success)
            {
                await LoadEmployeesAsync();
            }
        }
    }

    private async void OnMoveToSecondLine(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.BindingContext is string employeeName)
        {
            bool success = await _employeeService.MoveEmployeeAsync(employeeName, toSecondLine: true);

            if (success)
            {
                await LoadEmployeesAsync();
            }
        }
    }

    private async void OnMoveToFirstLine(object sender, EventArgs e)
    {
        if (sender is SwipeItem swipeItem && swipeItem.BindingContext is string employeeName)
        {
            bool success = await _employeeService.MoveEmployeeAsync(employeeName, toSecondLine: false);

            if (success)
            {
                await LoadEmployeesAsync();
            }
        }
    }
}