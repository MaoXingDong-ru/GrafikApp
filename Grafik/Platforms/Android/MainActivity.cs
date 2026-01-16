using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Grafik
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const string CHAT_CHANNEL_ID = "chat_messages_channel";
        private const string SHIFT_CHANNEL_ID = "shift_reminder_channel";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            
            // Создаём каналы уведомлений
            CreateNotificationChannels();
            
            // Запрашиваем разрешение на отправку уведомлений
            RequestNotificationPermissions();
        }

        /// <summary>
        /// Создаёт каналы уведомлений для Android 8.0+
        /// </summary>
        private void CreateNotificationChannels()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            try
            {
                var notificationManager = GetSystemService(NotificationService) as NotificationManager;

                if (notificationManager == null)
                    return;

                // Канал для сообщений чата
                var chatChannel = new NotificationChannel(
                    CHAT_CHANNEL_ID,
                    "Сообщения чата",
                    NotificationImportance.High)
                {
                    Description = "Уведомления о новых сообщениях в чате"
                };
                chatChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
                chatChannel.SetShowBadge(true);
                notificationManager.CreateNotificationChannel(chatChannel);

                // Канал для напоминаний о сменах
                var shiftChannel = new NotificationChannel(
                    SHIFT_CHANNEL_ID,
                    "Напоминания о сменах",
                    NotificationImportance.High)
                {
                    Description = "Уведомления о предстоящих сменах"
                };
                shiftChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
                shiftChannel.SetShowBadge(true);
                notificationManager.CreateNotificationChannel(shiftChannel);

                System.Diagnostics.Debug.WriteLine("[MainActivity] Каналы уведомлений созданы");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainActivity] Ошибка создания каналов: {ex.Message}");
            }
        }

        /// <summary>
        /// Запрашивает разрешение POST_NOTIFICATIONS для Android 13+
        /// </summary>
        private void RequestNotificationPermissions()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications)
                    != Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(this,
                        new[] { Manifest.Permission.PostNotifications }, 0);
                }
            }
        }

        /// <summary>
        /// Обработка результата запроса разрешений
        /// </summary>
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == 0)
            {
                if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] Разрешение POST_NOTIFICATIONS предоставлено");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] Разрешение POST_NOTIFICATIONS отклонено");
                }
            }
        }
    }
}