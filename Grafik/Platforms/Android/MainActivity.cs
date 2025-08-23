using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Grafik.Services;

namespace Grafik
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestNotificationPermissions();
        }

        private void RequestNotificationPermissions()
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
                if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications)
                    != Android.Content.PM.Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(this,
                        new[] { Manifest.Permission.PostNotifications }, 0);
                }
            }

#if ANDROID
            Grafik.Services.NotificationService.CreateNotificationChannel();
#endif
        }
    }
}