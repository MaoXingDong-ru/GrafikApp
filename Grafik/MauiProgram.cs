using Microsoft.Extensions.Logging;

#if ANDROID
using Grafik.Services;
#endif

namespace Grafik
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

#if ANDROID
            // Инициализируем канал уведомлений при запуске
            NotificationService.CreateNotificationChannel();
#endif

#if DEBUG
    			builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
