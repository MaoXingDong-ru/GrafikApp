using GrafikShared.Services;

namespace Grafik.Services;

/// <summary>Счётчик новых сообщений чата для главной страницы.</summary>
public static class ChatUnreadService
{
    private const string LastSeenKey = "ChatLastSeenTicks";

    // Вызывается при уходе со страницы чата
    public static void MarkSeen(DateTime lastMessageTime)
    {
        if (lastMessageTime > DateTime.MinValue)
            Preferences.Set(LastSeenKey, lastMessageTime.Ticks);
    }

    // null — нет связи / ошибка
    public static async Task<int?> GetUnreadCountAsync()
    {
        long ticks = Preferences.Get(LastSeenKey, 0L);
        if (ticks == 0)
            return 0; // чат ещё не открывали — считать нечего

        var url = Preferences.Get("FirebaseUrl", string.Empty);
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            var service = new FirebaseService(url);
            var messages = await service.GetMessagesAfterAsync(new DateTime(ticks));
            return messages.Count;
        }
        catch
        {
            return null;
        }
    }
}