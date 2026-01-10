using Grafik.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Grafik;

public partial class ChatPage : ContentPage
{
    private FirebaseService _firebaseService = null!;
    private ObservableCollection<FirebaseMessageViewModel> _messages = new();
    private CancellationTokenSource _cancellationTokenSource = null!;
    private DateTime _lastMessageTime = DateTime.MinValue;
    private string _currentUserName = string.Empty;

    public ChatPage()
    {
        InitializeComponent();
        Debug.WriteLine("[ChatPage] Constructor без параметров");
        MessagesCollectionView.ItemsSource = _messages;
        _currentUserName = Preferences.Get("SelectedEmployee", "Неизвестно");
    }

    public ChatPage(string userName)
    {
        InitializeComponent();
        Debug.WriteLine($"[ChatPage] Constructor с параметром: {userName}");
        MessagesCollectionView.ItemsSource = _messages;
        _currentUserName = userName;
        Title = $"Чат - {userName}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Debug.WriteLine("[ChatPage] OnAppearing");

        var firebaseUrl = Preferences.Get("FirebaseUrl", string.Empty);
        Debug.WriteLine($"[ChatPage] FirebaseUrl: {firebaseUrl}");

        if (string.IsNullOrEmpty(firebaseUrl))
        {
            Debug.WriteLine("[ChatPage] FirebaseUrl пуст! Silent mode - выходим без alert");
            await Navigation.PopAsync();
            return;
        }

        try
        {
            // Отменяем старый токен если существует
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Создаем новый экземпляр FirebaseService
            Debug.WriteLine("[ChatPage] Инициализация FirebaseService");
            _firebaseService = new FirebaseService(firebaseUrl);
            _cancellationTokenSource = new CancellationTokenSource();

            await LoadMessagesAsync();
            _ = PollMessagesAsync(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при инициализации: {ex.Message}");
            Debug.WriteLine($"[ChatPage] Stack: {ex.StackTrace}");
            // Silent mode - ошибки не показываем, только логируем
            await Navigation.PopAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Debug.WriteLine("[ChatPage] OnDisappearing");
        
        try
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Debug.WriteLine("[ChatPage] Отмена CancellationTokenSource");
                _cancellationTokenSource.Cancel();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при отмене: {ex.Message}");
        }
    }

    private async Task LoadMessagesAsync()
    {
        Debug.WriteLine("[ChatPage] LoadMessagesAsync");

        _messages.Clear();

        var messages = await _firebaseService.GetMessagesAsync();
        Debug.WriteLine($"[ChatPage] Загружено сообщений: {messages.Count}");

        foreach (var msg in messages)
        {
            var viewModel = new FirebaseMessageViewModel(msg);
            _messages.Add(viewModel);
            _lastMessageTime = msg.Timestamp;
        }

        if (_messages.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MessagesCollectionView.ScrollTo(_messages.Count - 1, position: ScrollToPosition.End, animate: false);
            });
        }
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine("[ChatPage] PollMessagesAsync старт");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, cancellationToken);
                var newMessages = await _firebaseService.GetMessagesAfterAsync(_lastMessageTime);

                if (newMessages.Count > 0)
                {
                    Debug.WriteLine($"[ChatPage] Новых сообщений: {newMessages.Count}");

                    foreach (var msg in newMessages)
                    {
                        var viewModel = new FirebaseMessageViewModel(msg);
                        _messages.Add(viewModel);
                        _lastMessageTime = msg.Timestamp;
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MessagesCollectionView.ScrollTo(_messages.Count - 1, position: ScrollToPosition.End, animate: true);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatPage] Ошибка полинга: {ex.Message}");
            }
        }
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnSendClicked ВЫЗВАН!");

        var messageText = MessageEntry.Text?.Trim();
        Debug.WriteLine($"[ChatPage] Текст: '{messageText}'");

        if (string.IsNullOrEmpty(messageText))
        {
            Debug.WriteLine("[ChatPage] Текст пуст, выход");
            return;
        }

        Debug.WriteLine($"[ChatPage] Сотрудник: {_currentUserName}");

        MessageEntry.Text = string.Empty;

        Debug.WriteLine("[ChatPage] Отправка...");
        var success = await _firebaseService.SendMessageAsync(_currentUserName, messageText);
        Debug.WriteLine($"[ChatPage] Результат: {success}");

        if (!success)
        {
            await DisplayAlert("Ошибка", "Не удалось отправить сообщение", "OK");
        }
        else
        {
            await LoadMessagesAsync();
        }
    }

    private async void OnShareFileClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnShareFileClicked");

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите файл расписания",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel" } },
                    { DevicePlatform.WinUI, new[] { ".xlsx", ".xls" } }
                })
            });

            if (result != null)
            {
                await OnShareFileAsync(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка выбора файла: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось выбрать файл: {ex.Message}", "OK");
        }
    }

    private async Task OnShareFileAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            // Проверяем размер (максимум 5 MB)
            if (fileInfo.Length > 5 * 1024 * 1024)
            {
                await DisplayAlert("Ошибка", $"Файл слишком большой: {fileInfo.Length / (1024 * 1024)} MB\n(максимум 5 MB)", "OK");
                return;
            }

            await DisplayAlert("Загрузка", $"Отправка файла {fileInfo.Name}...", "OK");

            var success = await _firebaseService.SendFileMessageAsync(_currentUserName, filePath);

            if (success)
            {
                await DisplayAlert("Успех", $"Файл {fileInfo.Name} успешно отправлен!", "OK");
                await LoadMessagesAsync();
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось отправить файл", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при загрузке файла: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось загрузить файл: {ex.Message}", "OK");
        }
    }

    private async void OnDownloadFileClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnDownloadFileClicked");

        try
        {
            if (sender is Button button && button.CommandParameter is FirebaseMessageViewModel messageVM)
            {
                Debug.WriteLine($"[ChatPage] Скачивание файла: {messageVM.FileName}");

                // Извлекаем файл из Firebase
                var filePath = await _firebaseService.ExtractFileAsync(messageVM.Message);

                if (string.IsNullOrEmpty(filePath))
                {
                    await DisplayAlert("Ошибка", "Не удалось скачать файл", "OK");
                    return;
                }

                Debug.WriteLine($"[ChatPage] Файл скачан: {filePath}");

                // Показываем диалог загрузки
                bool shouldLoad = await DisplayAlert(
                    "✅ Файл загружен",
                    $"📎 {Path.GetFileName(filePath)}\n\nЗагрузить расписание и заменить старые данные?",
                    "Загрузить",
                    "Отмена"
                );

                if (!shouldLoad)
                {
                    Debug.WriteLine("[ChatPage] Пользователь отказался загружать расписание");
                    return;
                }

                Debug.WriteLine("[ChatPage] Загрузка расписания из файла");

                // Способ 1: Ищем MainPage в стеке навигации (если есть NavigationPage)
                MainPage? mainPageInstance = null;

                if (App.Current?.MainPage is NavigationPage navigationPage)
                {
                    Debug.WriteLine("[ChatPage] Найдена NavigationPage");
                    mainPageInstance = navigationPage.Navigation.NavigationStack
                        .OfType<MainPage>()
                        .FirstOrDefault();

                    Debug.WriteLine($"[ChatPage] MainPage в стеке: {(mainPageInstance != null ? "найдена" : "не найдена")}");
                }

                // Способ 2: Если MainPage — прямой корень приложения
                if (mainPageInstance == null && App.Current?.MainPage is MainPage directMainPage)
                {
                    Debug.WriteLine("[ChatPage] MainPage найдена как корневая страница");
                    mainPageInstance = directMainPage;
                }

                // Если MainPage найдена, загружаем расписание
                if (mainPageInstance != null)
                {
                    Debug.WriteLine("[ChatPage] Найдена MainPage, загружаем и очищаем старые данные");

                    try
                    {
                        // Сначала удаляем старые данные
                        Debug.WriteLine("[ChatPage] Удаление старых данных...");
                        await mainPageInstance.ClearAllDataAsync();
                        
                        // Затем загружаем новое расписание
                        Debug.WriteLine("[ChatPage] Загрузка новых данных...");
                        await mainPageInstance.ProcessExcelFileAsync(filePath);

                        await DisplayAlert(
                            "✅ Успех", 
                            "Расписание успешно обновлено!\n\nСтарые данные удалены и новые загружены.", 
                            "OK"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatPage] Ошибка при обновлении: {ex.Message}");
                        await DisplayAlert("❌ Ошибка", $"Не удалось загрузить расписание:\n{ex.Message}", "OK");
                    }
                }
                else
                {
                    Debug.WriteLine("[ChatPage] MainPage не найдена");

                    // Сохраняем путь к файлу в Preferences для дальнейшей обработки
                    Preferences.Set("PendingScheduleFile", filePath);
                    Debug.WriteLine("[ChatPage] Файл сохранен в Preferences для дальнейшей загрузки");

                    // Показываем сообщение об успехе
                    await DisplayAlert(
                        "✅ Файл готов",
                        $"Файл расписания готов к загрузке.\n\nВозвращаюсь на главное меню...",
                        "OK"
                    );
                    
                    // Возвращаемся на главную страницу
                    Debug.WriteLine("[ChatPage] Возврат на главное меню");
                    await Navigation.PopToRootAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при скачивании файла: {ex.Message}");
            Debug.WriteLine($"[ChatPage] Stack: {ex.StackTrace}");
            await DisplayAlert("❌ Ошибка", $"Не удалось скачать файл:\n{ex.Message}", "OK");
        }
    }
}

/// <summary>
/// ViewModel для отображения сообщений в UI
/// </summary>
public class FirebaseMessageViewModel
{
    public FirebaseMessage Message { get; }

    public FirebaseMessageViewModel(FirebaseMessage message)
    {
        Message = message;
    }

    public string Sender => Message.Sender;
    public string Text => Message.Text;
    public DateTime Timestamp => Message.Timestamp;
    public string Id => Message.Id;
    public string? FileUrl => Message.FileData;
    public string? FileName => Message.FileName;
    public string Type => Message.Type;

    public bool IsFile => Message.Type == "file";

    public string FileSizeDisplay
    {
        get
        {
            if (Message.FileSize == 0) return "";

            if (Message.FileSize < 1024)
                return $"{Message.FileSize} B";
            if (Message.FileSize < 1024 * 1024)
                return $"{Message.FileSize / 1024.0:F1} KB";

            return $"{Message.FileSize / (1024.0 * 1024):F1} MB";
        }
    }
}