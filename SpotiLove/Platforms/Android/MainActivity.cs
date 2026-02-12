using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Diag = System.Diagnostics.Debug;

namespace SpotiLove;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "spotilove",
    DataHost = "auth")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Diag.WriteLine(" MainActivity OnCreate called");

        // Handle deep link if launched via intent
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        Diag.WriteLine("🔗 MainActivity OnNewIntent called!");
        Diag.WriteLine($"   Action: {intent?.Action}");
        Diag.WriteLine($"   Data: {intent?.Data}");
        Diag.WriteLine($"   DataString: {intent?.DataString}");

        if (intent != null)
        {
            Intent = intent; // Use property assignment instead of SetIntent
            HandleIntent(intent);
        }
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent?.Data != null)
        {
            var uri = intent.Data.ToString();
            Diag.WriteLine($" Received deep link: {uri}");

            // Store the URI to be processed when app is ready
            Preferences.Set("pending_deep_link", uri);
            Diag.WriteLine("💾 Stored deep link in preferences");

            // Try to process immediately
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    Diag.WriteLine("🔄 Triggering SpotifyAuthHandler...");
                    await Task.Delay(500); // Small delay to ensure app is ready
                    await SpotifyAuthHandler.HandleSpotifyCallback(uri);

                    // Clear the preference after successful handling
                    Preferences.Remove("pending_deep_link");
                }
                catch (Exception ex)
                {
                    Diag.WriteLine($" Error handling deep link in MainActivity: {ex.Message}");
                }
            });
        }
        else
        {
            Diag.WriteLine("Intent has no data");

            // Check for pending deep link
            var pendingLink = Preferences.Get("pending_deep_link", null);
            if (!string.IsNullOrEmpty(pendingLink))
            {
                Diag.WriteLine($" Found pending deep link: {pendingLink}");
                Preferences.Remove("pending_deep_link");

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(1000); // Wait for app to be ready
                    await SpotifyAuthHandler.HandleSpotifyCallback(pendingLink);
                });
            }
        }
    }
}