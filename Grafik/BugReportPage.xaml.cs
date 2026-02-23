using Grafik.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Grafik;

/// <summary>
/// ViewModel страницы для привязки IsDev и StatusOptions в DataTemplate
/// </summary>
public class BugReportPageViewModel
{
    public bool IsDev { get; set; }
    public List<string> StatusOptions { get; set; } = BugReport.StatusOptions;
}

public partial class BugReportPage : ContentPage
{
    private BugReportService? _service;
    private readonly ObservableCollection<BugReport> _reports = new();
    private string _currentUserName = string.Empty;

    public BugReportPage()
    {
        InitializeComponent();
        ReportsCollectionView.ItemsSource = _reports;
        _currentUserName = Preferences.Get("SelectedEmployee", "Аноним");

        BindingContext = new BugReportPageViewModel
        {
            IsDev = BugReport.IsDeveloper(_currentUserName)
        };

        TypePicker.ItemsSource = new List<string>
        {
            "🐛 Баг-репорт",
            "💡 Предложение"
        };
    }

    private BugReportService GetService()
    {
        if (_service == null)
        {
            var firebaseUrl = Preferences.Get("FirebaseUrl",
                "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");
            _service = new BugReportService(firebaseUrl);
        }
        return _service;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadReportsAsync();
    }

    private async Task LoadReportsAsync()
    {
        try
        {
            _reports.Clear();
            var reports = await GetService().GetBugReportsAsync();

            foreach (var report in reports)
            {
                _reports.Add(report);
            }

            Debug.WriteLine($"[BugReportPage] Загружено {_reports.Count} обращений");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BugReportPage] Ошибка загрузки: {ex.Message}");
            await DisplayAlert("Ошибка", "Не удалось загрузить обращения", "OK");
        }
    }

    /// <summary>
    /// Показать панель создания нового обращения
    /// </summary>
    private void OnAddClicked(object? sender, EventArgs e)
    {
        NewReportPanel.IsVisible = true;
        AddButton.IsVisible = false;
    }

    /// <summary>
    /// Скрыть панель и очистить поля
    /// </summary>
    private void OnCancelClicked(object? sender, EventArgs e)
    {
        ClearForm();
        NewReportPanel.IsVisible = false;
        AddButton.IsVisible = true;
    }

    /// <summary>
    /// Отправить баг-репорт / предложение
    /// </summary>
    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var title = TitleEntry.Text?.Trim();
        var steps = StepsEditor.Text?.Trim();

        if (string.IsNullOrEmpty(title))
        {
            await DisplayAlert("Ошибка", "Укажите тему обращения", "OK");
            return;
        }

        if (string.IsNullOrEmpty(steps))
        {
            await DisplayAlert("Ошибка", "Опишите проблему или предложение", "OK");
            return;
        }

        var reportType = TypePicker.SelectedIndex == 1 ? "feature" : "bug";

        var report = new BugReport
        {
            Type = reportType,
            Status = "open",
            Title = title,
            Steps = steps,
            Sender = _currentUserName,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var success = await GetService().SendBugReportAsync(report);

            if (success)
            {
                ClearForm();
                NewReportPanel.IsVisible = false;
                AddButton.IsVisible = true;

                await LoadReportsAsync();

                await DisplayAlert("Успех", "Обращение отправлено!", "OK");
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось отправить обращение", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BugReportPage] Ошибка отправки: {ex}");
            await DisplayAlert("Ошибка", $"Не удалось отправить: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Единая кнопка «Сохранить» — отправляет статус + комментарий одним действием
    /// </summary>
    private async void OnSaveDevResponseClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not BugReport report)
            return;

        if (string.IsNullOrEmpty(report.FirebaseKey))
            return;

        var picker = FindChildOfType<Picker>(button.Parent);
        var editor = FindChildOfType<Editor>(button.Parent);

        var newStatus = picker != null
            ? BugReport.StatusDisplayToValue(picker.SelectedIndex)
            : report.Status;

        var newComment = editor?.Text?.Trim() ?? report.DevComment ?? string.Empty;

        Debug.WriteLine($"[BugReportPage] Сохранение: status={newStatus}, comment={newComment}, picker found={picker != null}");

        try
        {
            var success = await GetService().UpdateStatusAndCommentAsync(
                report.FirebaseKey, newStatus, newComment);

            if (success)
            {
                await LoadReportsAsync();
                await DisplayAlert("Успех", "Ответ сохранён", "OK");
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось сохранить", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BugReportPage] Ошибка сохранения: {ex}");
            await DisplayAlert("Ошибка", $"Не удалось сохранить: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Рекурсивный поиск первого дочернего элемента указанного типа
    /// в визуальном дереве, начиная с родителя.
    /// Обходит Border, StackLayout и другие контейнеры.
    /// </summary>
    private static T? FindChildOfType<T>(object? parent) where T : View
    {
        if (parent is T found)
            return found;

        if (parent is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is T match)
                    return match;

                var nested = FindChildOfType<T>(child);
                if (nested != null)
                    return nested;
            }
        }
        else if (parent is Border border && border.Content is not null)
        {
            if (border.Content is T borderMatch)
                return borderMatch;

            return FindChildOfType<T>(border.Content);
        }
        else if (parent is ContentView contentView && contentView.Content is not null)
        {
            if (contentView.Content is T cvMatch)
                return cvMatch;

            return FindChildOfType<T>(contentView.Content);
        }

        return null;
    }

    private void ClearForm()
    {
        TitleEntry.Text = string.Empty;
        StepsEditor.Text = string.Empty;
        TypePicker.SelectedIndex = 0;
    }
}