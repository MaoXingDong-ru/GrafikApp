using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Grafik
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const string CHAT_CHANNEL_ID = "chat_messages_channel";
        private const string SHIFT_CHANNEL_ID = "shift_reminder_channel";
        private const int NOTIFICATION_PERMISSION_REQUEST_CODE = 1001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            
            // Создаём каналы уведомлений
            CreateNotificationChannels();
            
            // Запрашиваем разрешение на отправку уведомлений
            RequestNotificationPermissions();
        }

        protected override void OnResume()
        {
            base.OnResume();

            // Повторно проверяем разрешение при каждом возврате в приложение
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications)
                    != Permission.Granted)
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] ⚠️ POST_NOTIFICATIONS не предоставлен при OnResume");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] ✅ POST_NOTIFICATIONS предоставлен");
                }
            }
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
                chatChannel.EnableVibration(true);
                chatChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
                chatChannel.SetShowBadge(true);
                chatChannel.LockscreenVisibility = NotificationVisibility.Public;
                notificationManager.CreateNotificationChannel(chatChannel);

                // Канал для напоминаний о сменах
                var shiftChannel = new NotificationChannel(
                    SHIFT_CHANNEL_ID,
                    "Напоминания о сменах",
                    NotificationImportance.High)
                {
                    Description = "Уведомления о предстоящих сменах"
                };
                shiftChannel.EnableVibration(true);
                shiftChannel.SetVibrationPattern(new long[] { 0, 250, 250, 250 });
                shiftChannel.SetShowBadge(true);
                shiftChannel.LockscreenVisibility = NotificationVisibility.Public;
                notificationManager.CreateNotificationChannel(shiftChannel);

                System.Diagnostics.Debug.WriteLine("[MainActivity] ✅ Каналы уведомлений созданы");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainActivity] ❌ Ошибка создания каналов: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("[MainActivity] 📋 Запрашиваем POST_NOTIFICATIONS...");
                    ActivityCompat.RequestPermissions(this,
                        new[] { Manifest.Permission.PostNotifications }, NOTIFICATION_PERMISSION_REQUEST_CODE);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] ✅ POST_NOTIFICATIONS уже предоставлен");
                }
            }
        }

        /// <summary>
        /// Обработка результата запроса разрешений
        /// </summary>
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == NOTIFICATION_PERMISSION_REQUEST_CODE)
            {
                if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] ✅ POST_NOTIFICATIONS предоставлено пользователем");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainActivity] ❌ POST_NOTIFICATIONS отклонено пользователем!");
                    
                    // Показываем объяснение через MAUI
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        var mauiApp = Microsoft.Maui.Controls.Application.Current;
                        if (mauiApp?.MainPage != null)
                        {
                            bool openSettings = await mauiApp.MainPage.DisplayAlert(
                                "Уведомления отключены",
                                "Для получения уведомлений о новых сообщениях в чате необходимо разрешить уведомления в настройках.",
                                "Открыть настройки",
                                "Позже");

                            if (openSettings)
                            {
                                var intent = new Android.Content.Intent(
                                    Android.Provider.Settings.ActionAppNotificationSettings);
                                intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, PackageName);
                                StartActivity(intent);
                            }
                        }
                    });
                }
            }
        }
    }
}