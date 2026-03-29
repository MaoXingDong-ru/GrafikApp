using Grafik.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Grafik;

public partial class ChatPage : ContentPage
{
    private FirebaseService _firebaseService = null!;
    private ObservableCollection<FirebaseMessageViewModel> _messages = new();
    private ObservableCollection<FirebaseMessageViewModel> _pinnedMessages = new(); // Обычные закреплённые
    private ObservableCollection<FirebaseMessageViewModel> _pinnedScheduleFiles = new(); // Файлы расписания от админа
    private CancellationTokenSource _cancellationTokenSource = null!;
    private DateTime _lastMessageTime = DateTime.MinValue;
    private string _currentUserName = string.Empty;
    private bool _isEmojiPanelVisible = false;
    private FirebaseMessageViewModel? _replyToMessage = null;

    private static readonly string[] EmojiList =
    [
        "😀", "😂", "🤣", "😊", "😍", "🥰", "😎", "🤔",
        "😢", "😭", "😡", "🤯", "🥳", "😴", "🤗", "🤩",
        "👍", "👎", "👋", "🤝", "✌️", "🙏", "💪", "👏",
        "❤️", "🔥", "⭐", "✅", "❌", "⚠️", "💬", "📎",
        "🎉", "🎊", "🏆", "🎯", "💡", "📌", "🔔", "⏰"
    ];

    public ChatPage()
    {
        InitializeComponent();
        Debug.WriteLine("[ChatPage] Constructor без параметров");
        MessagesCollectionView.ItemsSource = _messages;
        PinnedMessagesCollectionView.ItemsSource = _pinnedMessages;
        PinnedScheduleFilesCollectionView.ItemsSource = _pinnedScheduleFiles;
        _currentUserName = Preferences.Get("SelectedEmployee", "Неизвестно");
        BuildEmojiPanel();
    }

    public ChatPage(string userName)
    {
        InitializeComponent();
        Debug.WriteLine($"[ChatPage] Constructor с параметром: {userName}");
        MessagesCollectionView.ItemsSource = _messages;
        PinnedMessagesCollectionView.ItemsSource = _pinnedMessages;
        PinnedScheduleFilesCollectionView.ItemsSource = _pinnedScheduleFiles;
        _currentUserName = userName;
        Title = $"Чат - {userName}";
        BuildEmojiPanel();
    }

    /// <summary>
    /// Построить сетку эмодзи в панели
    /// </summary>
    private void BuildEmojiPanel()
    {
        EmojiGrid.Children.Clear();
        EmojiGrid.ColumnDefinitions.Clear();
        EmojiGrid.RowDefinitions.Clear();

        const int columns = 8;
        int rows = (int)Math.Ceiling(EmojiList.Length / (double)columns);

        for (int c = 0; c < columns; c++)
            EmojiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int r = 0; r < rows; r++)
            EmojiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int i = 0; i < EmojiList.Length; i++)
        {
            var emoji = EmojiList[i];
            var btn = new Button
            {
                Text = emoji,
                FontSize = 22,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(2),
                Margin = new Thickness(1)
            };
            btn.Clicked += OnEmojiClicked;

            Grid.SetRow(btn, i / columns);
            Grid.SetColumn(btn, i % columns);
            EmojiGrid.Children.Add(btn);
        }
    }

    private void OnEmojiClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            var currentText = MessageEntry.Text ?? string.Empty;
            var cursorPos = MessageEntry.CursorPosition;

            if (cursorPos >= 0 && cursorPos <= currentText.Length)
            {
                MessageEntry.Text = currentText.Insert(cursorPos, btn.Text);
                MessageEntry.CursorPosition = cursorPos + btn.Text.Length;
            }
            else
            {
                MessageEntry.Text = currentText + btn.Text;
            }
        }
    }

    private void OnEmojiToggleClicked(object? sender, EventArgs e)
    {
        _isEmojiPanelVisible = !_isEmojiPanelVisible;
        EmojiPanel.IsVisible = _isEmojiPanelVisible;
        EmojiToggleButton.Text = _isEmojiPanelVisible ? "⌨️" : "😀";
    }

    private async void OnMessageLongPress(object sender, EventArgs e)
    {
        if (sender is Element element && element.BindingContext is FirebaseMessageViewModel messageVM)
        {
            string action = await DisplayActionSheet(
                "Действия с сообщением",
                "Отмена",
                null,
                "Ответить"
            );

            if (action == "Ответить")
            {
                ShowReplyPreview(messageVM);
            }
        }
    }

    private void ShowReplyPreview(FirebaseMessageViewModel message)
    {
        _replyToMessage = message;

        ReplyToSenderLabel.Text = $"↩️ Ответ для {message.Sender}";
        ReplyToTextLabel.Text = message.Text.Length > 100
            ? message.Text.Substring(0, 100) + "..."
            : message.Text;

        ReplyPreviewPanel.IsVisible = true;
        MessageEntry.Focus();
    }

    private void OnCancelReplyClicked(object sender, EventArgs e)
    {
        _replyToMessage = null;
        ReplyPreviewPanel.IsVisible = false;
    }

    private async void OnImageClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentUserName))
        {
            await DisplayAlert("Ошибка", "Пожалуйста, сначала выберите сотрудника на главной странице", "OK");
            return;
        }

        try
        {
            string action = await DisplayActionSheet("Отправить изображение", "Отмена", null,
                "📷 Сделать фото", "🖼️ Выбрать из галереи");

            FileResult? result = null;

            if (action == "📷 Сделать фото")
            {
                if (MediaPicker.Default.IsCaptureSupported)
                {
                    result = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
                    {
                        Title = "Сделайте фото"
                    });
                }
                else
                {
                    await DisplayAlert("Ошибка", "Камера не поддерживается на этом устройстве", "OK");
                    return;
                }
            }
            else if (action == "🖼️ Выбрать из галереи")
            {
                result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Выберите изображение"
                });
            }

            if (result != null)
            {
                await SendImageAsync(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка выбора изображения: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось выбрать изображение: {ex.Message}", "OK");
        }
    }

    private async Task SendImageAsync(string imagePath)
    {
        try
        {
            var fileInfo = new FileInfo(imagePath);

            if (fileInfo.Length > 3 * 1024 * 1024)
            {
                await DisplayAlert("Ошибка",
                    $"Изображение слишком большое: {fileInfo.Length / (1024 * 1024)} MB\n(максимум 3 MB)", "OK");
                return;
            }

            Debug.WriteLine($"[ChatPage] Отправка изображения: {fileInfo.Name} ({fileInfo.Length} байт)");

            var success = await _firebaseService.SendImageMessageAsync(_currentUserName, imagePath);

            if (success)
            {
                await LoadMessagesAsync();
            }
            else
            {
                await DisplayAlert("Ошибка", "Не удалось отправить изображение", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatPage] Ошибка при отправке изображения: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось отправить изображение: {ex.Message}", "OK");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Debug.WriteLine("[ChatPage] OnAppearing");

        App.IsChatPageActive = true;

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

            await BackgroundMessageService.Instance.MarkAllAsReadAsync();

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

        App.IsChatPageActive = false;

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
            var viewModel = new FirebaseMessageViewModel(msg, _currentUserName);
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

    /// <summary>
    /// ✅ ОБНОВЛЁННЫЙ метод: разделяем закреплённые сообщения на две категории
    /// </summary>
    private void UpdatePinnedMessages()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _pinnedMessages.Clear();
            _pinnedScheduleFiles.Clear();

            // Берём все сообщения, у которых либо пользовательское закрепление, либо админское расписание
            var all = _messages.ToList();

            foreach (var msg in all)
            {
                // Сначала проверяем специальный админский флаг -> отдельная панель админа
                if (msg.IsFile && msg.IsAdminSchedulePinned)
                {
                    _pinnedScheduleFiles.Add(msg);
                    continue;
                }

                // Обычные закрепления пользователей
                if (msg.IsMessagePinned)
                {
                    _pinnedMessages.Add(msg);
                }
            }

            // Показываем панели только если есть соответствующие сообщения
            PinnedMessagesFrame.IsVisible = _pinnedMessages.Count > 0;

            if (this.FindByName("PinnedScheduleFilesPanel") is VisualElement schedulePanel)
            {
                schedulePanel.IsVisible = _pinnedScheduleFiles.Count > 0;
            }

            Debug.WriteLine($"[ChatPage] Обычных закреплённых: {_pinnedMessages.Count}, Файлов расписания: {_pinnedScheduleFiles.Count}");
        });
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnSendClicked ВЫЗВАН!");

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

        _isEmojiPanelVisible = false;
        EmojiPanel.IsVisible = false;
        EmojiToggleButton.Text = "😀";

        Debug.WriteLine("[ChatPage] Отправка...");

        bool success;

        if (_replyToMessage != null)
        {
            success = await _firebaseService.SendReplyMessageAsync(
                _currentUserName,
                messageText,
                _replyToMessage.Id,
                _replyToMessage.Text,
                _replyToMessage.Sender);

            OnCancelReplyClicked(sender, e);
        }
        else
        {
            success = await _firebaseService.SendMessageAsync(_currentUserName, messageText);
        }

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

            if (fileInfo.Length > 5 * 1024 * 1024)
            {
                await DisplayAlert("Ошибка", $"Файл слишком большой: {fileInfo.Length / (1024 * 1024)} MB\n(максимум 5 MB)", "OK");
                return;
            }

            await DisplayAlert("Загрузка", $"Отправка файла {fileInfo.Name}...", "OK");

            var success = await _firebaseService.SendFileMessageAsync(_currentUserName, filePath, autoPinForAdmin: true);

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

    private async void OnPinFileClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnPinFileClicked");

        if (sender is Button button && button.CommandParameter is FirebaseMessageViewModel messageVM)
        {
            messageVM.IsMessagePinned = !messageVM.IsMessagePinned;
            messageVM.Message.IsPinned = messageVM.IsMessagePinned;

            Debug.WriteLine($"[ChatPage] Сообщение {messageVM.Id} теперь {(messageVM.IsMessagePinned ? "закреплено" : "откреплено")}");

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

    /// <summary>
    /// ✅ Скролл к закреплённому сообщению при клике на превью
    /// </summary>
    private async void OnPinnedMessageClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("[ChatPage] OnPinnedMessageClicked");

        if (sender is not View view || view.BindingContext is not FirebaseMessageViewModel messageVM)
            return;

        var index = _messages.IndexOf(messageVM);
        if (index >= 0)
        {
            Debug.WriteLine($"[ChatPage] Скролл к закреплённому сообщению на индекс {index}");

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                MessagesCollectionView.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: true);

                await Task.Delay(200);
            });
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

                var filePath = await _firebaseService.ExtractFileAsync(messageVM.Message);

                if (string.IsNullOrEmpty(filePath))
                {
                    await DisplayAlert("Ошибка", "Не удалось скачать файл", "OK");
                    return;
                }

                Debug.WriteLine($"[ChatPage] Файл скачан: {filePath}");

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

                MainPage? mainPageInstance = null;

                if (App.Current?.MainPage is NavigationPage navigationPage)
                {
                    Debug.WriteLine("[ChatPage] Найдена NavigationPage");
                    mainPageInstance = navigationPage.Navigation.NavigationStack
                        .OfType<MainPage>()
                        .FirstOrDefault();

                    Debug.WriteLine($"[ChatPage] MainPage в стеке: {(mainPageInstance != null ? "найдена" : "не найдена")}");
                }

                if (mainPageInstance == null && App.Current?.MainPage is MainPage directMainPage)
                {
                    Debug.WriteLine("[ChatPage] MainPage найдена как корневая страница");
                    mainPageInstance = directMainPage;
                }

                if (mainPageInstance != null)
                {
                    Debug.WriteLine("[ChatPage] Найдена MainPage, загружаем и очищаем старые данные");

                    try
                    {
                        Debug.WriteLine("[ChatPage] Удаление старых данных...");
                        await mainPageInstance.ClearAllDataAsync();

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

                    Preferences.Set("PendingScheduleFile", filePath);
                    Debug.WriteLine("[ChatPage] Файл сохранен в Preferences для дальнейшей загрузки");

                    await DisplayAlert(
                        "Файл готов",
                        $"Файл расписания готов к загрузке.\n\nВозвращаюсь на главное меню...",
                        "OK"
                    );

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
                        var viewModel = new FirebaseMessageViewModel(msg, _currentUserName);
                        _messages.Add(viewModel);
                        _lastMessageTime = msg.Timestamp;
                    }

                    await _firebaseService.MarkMessagesAsReadAsync(newMessages);

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
    private readonly string _currentUserName;

    public FirebaseMessage Message { get; }

    public FirebaseMessageViewModel(FirebaseMessage message, string currentUserName)
    {
        Message = message;
        _isMessagePinned = message.IsPinned;
        _currentUserName = currentUserName;
    }

    public string Sender => Message.Sender;
    public string Text => Message.Text;
    public DateTime Timestamp => Message.Timestamp;
    public string Id => Message.Id;
    public string? FileUrl => Message.FileData;
    public string? FileName => Message.FileName;
    public string Type => Message.Type;

    public string? ReplyToId => Message.ReplyToId;
    public string? ReplyToText => Message.ReplyToText;
    public string? ReplyToSender => Message.ReplyToSender;
    public bool HasReply => !string.IsNullOrEmpty(ReplyToId);

    public bool IsFile => Message.Type == "file";
    public bool IsImage => Message.Type == "image";
    public bool IsText => Message.Type == "text";

    public string? ImageData => IsImage ? Message.FileData : null;

    /// <summary>
    /// Моё сообщение? (Sender совпадает с текущим пользователем)
    /// </summary>
    public bool IsMine => string.Equals(Sender, _currentUserName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Не моё сообщение (для отображения аватарки и имени)
    /// </summary>
    public bool IsNotMine => !IsMine;

    /// <summary>
    /// Проверка, является ли отправитель администратором
    /// </summary>
    public bool IsFromAdmin => Sender.Contains("Администратор", StringComparison.OrdinalIgnoreCase) ||
                               Sender.Contains("Admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Файл от обычного пользователя (НЕ закреплённый файл расписания от админа)
    /// </summary>
    public bool IsFileButNotAdminSchedule => IsFile && !IsAdminSchedulePinned;

    /// <summary>
    /// Превью текста для закреплённого сообщения (первая строка, макс 60 символов)
    /// </summary>
    public string PinnedPreviewText
    {
        get
        {
            var text = Text ?? FileName ?? "Сообщение";
            var firstLine = text.Split('\n')[0];
            return firstLine.Length > 60 ? firstLine.Substring(0, 60) + "..." : firstLine;
        }
    }

    public string Initials
    {
        get
        {
            var parts = Sender.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            else if (parts.Length == 1)
                return parts[0][0].ToString().ToUpper();

            return "??";
        }
    }

    // Новый флаг: сообщение помечено как админское расписание (из FirebaseMessage.IsAdminSchedulePinned)
    public bool IsAdminSchedulePinned
    {
        get => Message.IsAdminSchedulePinned;
        set
        {
            if (Message.IsAdminSchedulePinned != value)
            {
                Message.IsAdminSchedulePinned = value;
                OnPropertyChanged(nameof(IsAdminSchedulePinned));
                // Влияет на отображение файлов в основном списке
                OnPropertyChanged(nameof(IsFileButNotAdminSchedule));
            }
        }
    }

    public bool IsMessagePinned
    {
        get => _isMessagePinned;
        set
        {
            if (_isMessagePinned != value)
            {
                _isMessagePinned = value;
                Message.IsPinned = value; // синхронизируем с моделью
                OnPropertyChanged(nameof(IsMessagePinned));
                OnPropertyChanged(nameof(PinButtonText));
                OnPropertyChanged(nameof(IsFileButNotAdminSchedule));
            }
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
}