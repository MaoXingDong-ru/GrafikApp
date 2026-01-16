using Grafik.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace Grafik;

public partial class ChatPage : ContentPage
{
    private FirebaseService _firebaseService = null!;
    private ObservableCollection<FirebaseMessageViewModel> _messages = new();
    private ObservableCollection<FirebaseMessageViewModel> _pinnedMessages = new();
    private CancellationTokenSource _cancellationTokenSource = null!;
    private DateTime _lastMessageTime = DateTime.MinValue;
    private string _currentUserName = string.Empty;

    public ChatPage()
    {
        InitializeComponent();
        Debug.WriteLine("[ChatPage] Constructor без параметров");
        MessagesCollectionView.ItemsSource = _messages;
        PinnedMessagesCollectionView.ItemsSource = _pinnedMessages;
        _currentUserName = Preferences.Get("SelectedEmployee", "Неизвестно");
    }

    public ChatPage(string userName)
    {
        InitializeComponent();
        Debug.WriteLine($"[ChatPage] Constructor с параметром: {userName}");
        MessagesCollectionView.ItemsSource = _messages;
        PinnedMessagesCollectionView.ItemsSource = _pinnedMessages;
        _currentUserName = userName;
        Title = $"Чат - {userName}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Debug.WriteLine("[ChatPage] OnAppearing");

        // Проверяем, установлено ли имя пользователя
        _currentUserName = Preferences.Get("SelectedEmployee", string.Empty);
        
        bool isUserSelected = !string.IsNullOrEmpty(_currentUserName);
        
        if (!isUserSelected)
        {
            Debug.WriteLine("[ChatPage] Пользователь не выбран - отключаем ввод");
            MessageEntry.IsEnabled = false;
            MessageEntry.Placeholder = "Сначала выберите сотрудника";
            SendButton.IsEnabled = false;
            ShareButton.IsEnabled = false;
        }
        else
        {
            Debug.WriteLine($"[ChatPage] Пользователь: {_currentUserName}");
            MessageEntry.IsEnabled = true;
            SendButton.IsEnabled = true;
            ShareButton.IsEnabled = true;
        }

        var firebaseUrl = Preferences.Get("FirebaseUrl", "https://grafikchat-92791-default-rtdb.europe-west1.firebasedatabase.app/");
        Debug.WriteLine($"[ChatPage] FirebaseUrl: {firebaseUrl}");

        try
        {
            Debug.WriteLine("[ChatPage] Инициализация FirebaseService");
            _firebaseService = new FirebaseService(firebaseUrl);
            _cancellationTokenSource = new CancellationTokenSource();

            await LoadMessagesAsync();
            
            // ❌ НЕ полагаемся на фоновый сервис
            // Просто загружаем сообщения и показываем их
            _ = PollMessagesAsync(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при инициализации: {ex.Message}");
            Debug.WriteLine($"[ChatPage] Stack: {ex.StackTrace}");
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

        UpdatePinnedMessages();

        if (_messages.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MessagesCollectionView.ScrollTo(_messages.Count - 1, position: ScrollToPosition.End, animate: false);
            });
        }
    }


    private void UpdatePinnedMessages()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _pinnedMessages.Clear();
            foreach (var msg in _messages.Where(m => m.IsMessagePinned))
            {
                _pinnedMessages.Add(msg);
            }
            PinnedMessagesFrame.IsVisible = _pinnedMessages.Count > 0;
        });
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnSendClicked ВЫЗВАН!");

        // Проверяем, выбран ли пользователь
        if (string.IsNullOrEmpty(_currentUserName))
        {
            await DisplayAlert("Ошибка", "Пожалуйста, сначала выберите сотрудника на главной странице", "OK");
            return;
        }

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
            // 🔴 Уведомление об ошибке
            NotificationService.ShowInstantNotification(
                "❌ Ошибка отправки",
                "Не удалось отправить сообщение",
                NotificationService.CHAT_CHANNEL_ID);

            await DisplayAlert("Ошибка", "Не удалось отправить сообщение", "OK");
        }
        else
        {
            // ✅ Уведомление об успехе (опционально)
            NotificationService.ShowInstantNotification(
                "✅ Сообщение отправлено",
                messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText,
                NotificationService.CHAT_CHANNEL_ID);

            await LoadMessagesAsync();
        }
    }

    private async void OnShareFileClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnShareFileClicked");

        // Проверяем, выбран ли пользователь
        if (string.IsNullOrEmpty(_currentUserName))
        {
            await DisplayAlert("Ошибка", "Пожалуйста, сначала выберите сотрудника на главной странице", "OK");
            return;
        }

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
                // 🔔 Уведомление об успешной отправке
                NotificationService.ShowInstantNotification(
                    "✅ Файл отправлен",
                    fileInfo.Name,
                    NotificationService.CHAT_CHANNEL_ID);

                await DisplayAlert("Успех", $"Файл {fileInfo.Name} успешно отправлен!", "OK");
                await LoadMessagesAsync();
            }
            else
            {
                // 🔴 Уведомление об ошибке
                NotificationService.ShowInstantNotification(
                    "❌ Ошибка отправки",
                    $"Не удалось отправить {fileInfo.Name}",
                    NotificationService.CHAT_CHANNEL_ID);

                await DisplayAlert("Ошибка", "Не удалось отправить файл", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при загрузке файла: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось загрузить файл: {ex.Message}", "OK");
        }
    }

    private async void OnPinFileClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnPinFileClicked");

        if (sender is Button button && button.CommandParameter is FirebaseMessageViewModel messageVM)
        {
            messageVM.IsMessagePinned = !messageVM.IsMessagePinned;
            messageVM.Message.IsPinned = messageVM.IsMessagePinned;

            Debug.WriteLine($"[ChatPage] Сообщение {messageVM.Id} теперь {(messageVM.IsMessagePinned ? "закреплено" : "открепленоклено")}");

            // Обновляем только поле isPinned через PATCH
            var success = await _firebaseService.UpdateMessagePinnedStatusAsync(messageVM.Message);
            if (!success)
            {
                messageVM.IsMessagePinned = !messageVM.IsMessagePinned;
                messageVM.Message.IsPinned = messageVM.IsMessagePinned;
                await DisplayAlert("Ошибка", "Не удалось обновить статус закрепления", "OK");
            }

            UpdatePinnedMessages();
        }
    }

    private async void OnPinnedMessageClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnPinnedMessageClicked");

        if (sender is Button button && button.CommandParameter is FirebaseMessageViewModel messageVM)
        {
            // Ищем индекс сообщения в основном списке
            var index = _messages.IndexOf(messageVM);
            if (index >= 0)
            {
                Debug.WriteLine($"[ChatPage] Скролл к закреплённому сообщению на индекс {index}");

                // Скролим к сообщению с небольшой анимацией
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    MessagesCollectionView.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: true);
                    
                    // Добавляем визуальное выделение
                    HighlightMessage(messageVM);
                    
                    // Убираем выделение через 2 секунды
                    await Task.Delay(2000);
                    RemoveHighlightMessage(messageVM);
                });
            }
        }
    }

    private void HighlightMessage(FirebaseMessageViewModel messageVM)
    {
        messageVM.IsHighlighted = true;
    }

    private void RemoveHighlightMessage(FirebaseMessageViewModel messageVM)
    {
        messageVM.IsHighlighted = false;
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
                    "Файл загружен",
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
                            "Успех", 
                            "Расписание успешно обновлено!\n\nСтарые данные удалены и новые загружены.", 
                            "OK"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatPage] Ошибка при обновлении: {ex.Message}");
                        await DisplayAlert("Ошибка", $"Не удалось загрузить расписание:\n{ex.Message}", "OK");
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
                        "Файл готов",
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
            await DisplayAlert("Ошибка", $"Не удалось скачать файл:\n{ex.Message}", "OK");
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

                        // ❌ НЕ показываем уведомление здесь
                        // Уведомление показывается из App.xaml.cs через фоновый сервис
                    }

                    UpdatePinnedMessages();

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
}

/// <summary>
/// ViewModel для отображения сообщений в UI
/// </summary>
public class FirebaseMessageViewModel : INotifyPropertyChanged
{
    private bool _isHighlighted;
    private bool _isMessagePinned;

    public FirebaseMessage Message { get; }

    public FirebaseMessageViewModel(FirebaseMessage message)
    {
        Message = message;
        _isMessagePinned = message.IsPinned;
    }

    public string Sender => Message.Sender;
    public string Text => Message.Text;
    public DateTime Timestamp => Message.Timestamp;
    public string Id => Message.Id;
    public string? FileUrl => Message.FileData;
    public string? FileName => Message.FileName;
    public string Type => Message.Type;

    public bool IsFile => Message.Type == "file";

    public bool IsMessagePinned
    {
        get => _isMessagePinned;
        set
        {
            if (_isMessagePinned != value)
            {
                _isMessagePinned = value;
                OnPropertyChanged(nameof(IsMessagePinned));
                OnPropertyChanged(nameof(PinButtonText));
            }
        }
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                OnPropertyChanged(nameof(IsHighlighted));
            }
        }
    }

    public string PinButtonText => IsMessagePinned ? "Откреп." : "Закреп.";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}