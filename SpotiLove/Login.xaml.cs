using Microsoft.Maui.Controls;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Diagnostics;

namespace SpotiLove;

public partial class Login : ContentPage
{
    private readonly HttpClient _httpClient;
    public const string API_BASE_URL = "https://spotilove.danielnaz.com";

    public Login()
    {
        InitializeComponent();
        _httpClient = new HttpClient { BaseAddress = new Uri(API_BASE_URL) };

        //  Check for pending deep links when login page loads
        CheckForPendingDeepLink();
    }

    private async void CheckForPendingDeepLink()
    {
        try
        {
            await Task.Delay(500); // Give UI time to load

            var pendingLink = Preferences.Get("pending_deep_link", null);
            if (!string.IsNullOrEmpty(pendingLink))
            {
                Debug.WriteLine($" Processing pending deep link: {pendingLink}");
                Preferences.Remove("pending_deep_link");
                await SpotifyAuthHandler.HandleSpotifyCallback(pendingLink);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error checking pending deep link: {ex.Message}");
        }
    }

    public async void OnGoToSignUp(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new SignUp());
    }

    private async void OnSignIn(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(email))
        {
            await DisplayAlert("Error", "Please enter your email or username", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Error", "Please enter your password", "OK");
            return;
        }

        ContentPage loadingPage = null!;
        try
        {
            loadingPage = new ContentPage
            {
                Content = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 20,
                    Children =
                    {
                        new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#1db954"), WidthRequest = 50, HeightRequest = 50 },
                        new Label { Text = "Signing in...", TextColor = Colors.White, FontSize = 16 }
                    }
                },
                BackgroundColor = Color.FromArgb("#121212")
            };

            await Navigation.PushModalAsync(loadingPage, false);

            var loginData = new
            {
                Email = email,
                Password = password,
                RememberMe = RememberMeCheckBox.IsChecked
            };

            var json = JsonSerializer.Serialize(loginData);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/auth/login", content);
            await Navigation.PopModalAsync(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                try
                {
                    var err = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    await DisplayAlert("Login Failed", err?.Message ?? "Unable to login. Please try again.", "OK");
                }
                catch
                {
                    await DisplayAlert("Login Failed", "Unable to login. Please try again.", "OK");
                }
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loginResponse?.Success == true && loginResponse.User != null)
            {
                UserData.Current = new UserData
                {
                    Id = loginResponse.User.Id,
                    Name = loginResponse.User.Name,
                    Email = loginResponse.User.Email,
                    Age = loginResponse.User.Age
                };

                await SecureStorage.SetAsync("user_id", UserData.Current.Id.ToString());
                if (!string.IsNullOrEmpty(UserData.Current.Name))
                    await SecureStorage.SetAsync("user_name", UserData.Current.Name);
                if (!string.IsNullOrEmpty(UserData.Current.Email))
                    await SecureStorage.SetAsync("user_email", UserData.Current.Email);

                if (!string.IsNullOrEmpty(loginResponse.Token))
                    await SecureStorage.SetAsync("auth_token", loginResponse.Token);

                await NavigateToMainPage();
                return;
            }
            else
            {
                await DisplayAlert("Login Failed", loginResponse?.Message ?? "Invalid credentials", "OK");
            }
        }
        catch (HttpRequestException)
        {
            if (loadingPage != null) await Navigation.PopModalAsync(false);
            await DisplayAlert("Connection Error", "Unable to connect to the server. Check your network.", "OK");
        }
        catch (Exception ex)
        {
            if (loadingPage != null) await Navigation.PopModalAsync(false);
            await DisplayAlert("Error", $"Login failed: {ex.Message}", "OK");
        }
    }

    private async void OnSpotifyLogin(object sender, EventArgs e)
    {
        try
        {
            Debug.WriteLine("🎵 Spotify login button clicked");

            var spotifyLoginUrl = $"{API_BASE_URL}/login";
            Debug.WriteLine($"🔗 Opening URL: {spotifyLoginUrl}");

            var browserOptions = new BrowserLaunchOptions
            {
                LaunchMode = BrowserLaunchMode.SystemPreferred,
                TitleMode = BrowserTitleMode.Show,
                PreferredToolbarColor = Color.FromArgb("#1db954"),
                PreferredControlColor = Colors.White
            };

            var result = await Browser.OpenAsync(spotifyLoginUrl, browserOptions);
            Debug.WriteLine($" Browser.OpenAsync result: {result}");

            if (!result)
            {
                Debug.WriteLine(" Browser failed to open");
                await DisplayAlert("Error", "Could not open Spotify login page.", "OK");
            }
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                Debug.WriteLine("Shell.Current is null, creating new AppShell");
                Application.Current.MainPage = new AppShell();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Spotify login error: {ex.Message}");
            await DisplayAlert("Error", $"Failed to open Spotify login: {ex.Message}", "OK");
        }
    }

    private async void OnTestDeepLink(object sender, EventArgs e)
    {
        try
        {
            // Create a test URI that mimics what Spotify would send back
            var testUrl = "spotilove://auth/success?token=test-token-123&userId=00000000-0000-0000-0000-000000000001&isNewUser=false&name=Test%20User";

            Debug.WriteLine($"Testing deep link: {testUrl}");

            // Directly call the handler instead of trying to open the URL
            await SpotifyAuthHandler.HandleSpotifyCallback(testUrl);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Test Failed", ex.Message, "OK");
        }
    }

    private async void OnForgotPassword(object sender, EventArgs e)
    {
        var email = await DisplayPromptAsync("Forgot Password", "Enter your email address:", "Send Reset Link", "Cancel", keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        try
        {
            var resetData = new { Email = email.Trim() };
            var json = JsonSerializer.Serialize(resetData);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/auth/forgot-password", content);
            if (response.IsSuccessStatusCode)
                await DisplayAlert("Success", "Password reset link sent to your email!", "OK");
            else
                await DisplayAlert("Error", "Unable to send reset link. Please try again.", "OK");
        }
        catch
        {
            await DisplayAlert("Error", "Password reset functionality is currently unavailable.", "OK");
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnGoogleLogin(object sender, EventArgs e)
    {
        await DisplayAlert("Google Login", "Coming soon", "OK");
    }

    private async void OnAppleLogin(object sender, EventArgs e)
    {
        await DisplayAlert("Apple Login", "Coming soon", "OK");
    }

    private async void OnDemoUser(object sender, EventArgs e)
    {
        EmailEntry.Text = "demo@spotilove.com";
        PasswordEntry.Text = "demo123";
        await DisplayAlert("Demo User", "Demo credentials loaded. Click Sign In to continue.", "OK");
    }

    private async void OnGuestMode(object sender, EventArgs e)
    {
        var result = await DisplayAlert("Guest Mode", "Continue without an account? You'll have limited features.", "Continue", "Cancel");
        if (result)
        {
            await SecureStorage.SetAsync("is_guest", "true");
            await NavigateToMainPage();
        }
    }

    private async Task NavigateToMainPage()
    {
        try
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//MainPage");
            }
            else
            {
                Debug.WriteLine("Shell.Current is null, creating new AppShell");
                Application.Current.MainPage = new AppShell();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Navigation error: {ex.Message}");
            Application.Current.MainPage = new NavigationPage(new MainPage());
        }
    }

    private class LoginResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public UserDto? User { get; set; }
    }

    private class ErrorResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}