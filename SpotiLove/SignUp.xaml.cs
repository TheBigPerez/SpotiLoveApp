using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace SpotiLove;

public partial class SignUp : ContentPage
{
    private readonly HttpClient _httpClient;
    private const string API_BASE_URL = "https://spotilove.danielnaz.com";
    private string? _selectedImageBase64;

    public SignUp()
    {
        InitializeComponent();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(API_BASE_URL),
            Timeout = TimeSpan.FromSeconds(30)
        };

        BioEditor.TextChanged += OnBioTextChanged;
    }

    private void OnBioTextChanged(object sender, TextChangedEventArgs e)
    {
        if (BioCharCountLabel == null) return;
        int len = e.NewTextValue?.Length ?? 0;
        BioCharCountLabel.Text = $"{len}/500 characters";
        BioCharCountLabel.TextColor = len > 450
            ? Color.FromArgb("#ff4444")
            : len > 400
                ? Color.FromArgb("#ffaa00")
                : Color.FromArgb("#888888");
    }

    private async void OnGoToLogin(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//Login");

    private async void OnBackClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//Login");

    private async void OnCreateAccount(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        var email = EmailEntry.Text?.Trim();
        var ageText = AgeEntry.Text?.Trim();
        var gender = GenderPicker.SelectedItem?.ToString();
        var sexualOrientation = SexualOrientationPicker.SelectedItem?.ToString();
        var bio = BioEditor.Text?.Trim();
        var password = PasswordEntry.Text;
        var confirmPassword = ConfirmPasswordEntry.Text;
        var termsAccepted = TermsCheckBox.IsChecked;

        var validationError = ValidateForm(name, email, ageText, gender, sexualOrientation,
                                           password, confirmPassword, termsAccepted);
        if (validationError != null)
        {
            await DisplayAlert("Validation Error", validationError, "OK");
            return;
        }

        int age = int.Parse(ageText!);

        ContentPage? loadingPage = null;
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
                        new Label { Text = "Creating your account...", TextColor = Colors.White, FontSize = 16 }
                    }
                },
                BackgroundColor = Color.FromArgb("#121212")
            };

            await Navigation.PushModalAsync(loadingPage, false);

            // Build request — only include image if small enough
            object registerData;
            if (_selectedImageBase64 != null && _selectedImageBase64.Length > 500_000)
            {
                // Image too large, skip it
                registerData = new { Name = name, Email = email, Password = password, Age = age,
                                     Gender = gender, SexualOrientation = sexualOrientation, Bio = bio };
            }
            else
            {
                registerData = new { Name = name, Email = email, Password = password, Age = age,
                                     Gender = gender, SexualOrientation = sexualOrientation, Bio = bio,
                                     ProfileImage = _selectedImageBase64 };
            }

            var json = JsonSerializer.Serialize(registerData);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            Debug.WriteLine($"Sending register request to {API_BASE_URL}/auth/register");
            Debug.WriteLine($"Body: {json[..Math.Min(200, json.Length)]}");

            var response = await _httpClient.PostAsync("/auth/register", content);

            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(false);

            var responseBody = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"Register response [{(int)response.StatusCode}]: {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                await HandleSuccessfulRegistration(responseBody);
            }
            else
            {
                // Show the actual server error so we can diagnose
                string userMessage = ExtractErrorMessage(responseBody, response.StatusCode);
                await DisplayAlert("Registration Failed", userMessage, "OK");
            }
        }
        catch (HttpRequestException httpEx)
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(false);
            Debug.WriteLine($"HTTP error: {httpEx.Message}");
            await DisplayAlert("Connection Error",
                "Unable to connect to the server. Check your internet connection.", "OK");
        }
        catch (TaskCanceledException)
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(false);
            await DisplayAlert("Timeout", "Request timed out. Please try again.", "OK");
        }
        catch (Exception ex)
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(false);
            Debug.WriteLine($"Unexpected error: {ex.Message}\n{ex.StackTrace}");
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

    private async Task HandleSuccessfulRegistration(string responseBody)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var registerResponse = JsonSerializer.Deserialize<RegisterResponse>(responseBody, opts);

            if (registerResponse?.Success == true && registerResponse.User != null)
            {
                // Save session
                await SecureStorage.SetAsync("user_id", registerResponse.User.Id.ToString());
                await SecureStorage.SetAsync("user_email", registerResponse.User.Email ?? "");
                await SecureStorage.SetAsync("user_name", registerResponse.User.Name ?? "");
                if (!string.IsNullOrEmpty(registerResponse.Token))
                    await SecureStorage.SetAsync("auth_token", registerResponse.Token);

                UserData.Current = new UserData
                {
                    Id = registerResponse.User.Id,
                    Name = registerResponse.User.Name,
                    Email = registerResponse.User.Email,
                    Age = registerResponse.User.Age
                };

                await DisplayAlert("Success",
                    $"Welcome to SpotiLove, {registerResponse.User.Name ?? ""}! Let's set up your music taste.",
                    "Get Started");

                await Navigation.PushAsync(new ArtistSelectionPage());
            }
            else
            {
                await DisplayAlert("Registration Failed",
                    registerResponse?.Message ?? "Unexpected response from server.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Response parse error: {ex.Message}");
            Debug.WriteLine($"Raw body: {responseBody}");
            await DisplayAlert("Error",
                $"Server responded but response couldn't be read. Raw: {responseBody[..Math.Min(200, responseBody.Length)]}", "OK");
        }
    }

    /// Extracts a human-readable message from any server error response format.
    private static string ExtractErrorMessage(string body, System.Net.HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(body))
            return $"Server returned error {(int)status} with no details.";

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Try our own format: { message: "..." }
            if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                return msgProp.GetString() ?? body;

            // Try ProblemDetails format: { detail: "..." }
            if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                return detailProp.GetString() ?? body;

            // Try ProblemDetails title
            if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                return titleProp.GetString() ?? body;
        }
        catch
        {
            // Body wasn't JSON — show raw (truncated)
        }

        return body.Length > 300 ? body[..300] + "..." : body;
    }

    private async void OnPickImageClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = FilePickerFileType.Images,
                PickerTitle = "Choose a profile picture"
            });

            if (result == null) return;

            using var stream = await result.OpenReadAsync();
            ProfileImagePreview.Source = ImageSource.FromStream(() => stream);

            using var ms = new MemoryStream();
            stream.Seek(0, SeekOrigin.Begin);
            await stream.CopyToAsync(ms);
            _selectedImageBase64 = Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to pick image: {ex.Message}", "OK");
        }
    }

    private string? ValidateForm(string? name, string? email, string? ageText, string? gender,
                                  string? sexualOrientation, string? password,
                                  string? confirmPassword, bool termsAccepted)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 50)
            return "Name must be between 2 and 50 characters long";
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            return "Please enter a valid email address";
        if (string.IsNullOrWhiteSpace(ageText) || !int.TryParse(ageText, out int age) || age < 18 || age > 120)
            return "Please enter a valid age between 18 and 120";
        if (string.IsNullOrWhiteSpace(gender))
            return "Please select your gender";
        if (!string.IsNullOrWhiteSpace(sexualOrientation) && !IsValidOrientation(sexualOrientation))
            return "Please select a valid sexual orientation";
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6 || password.Length > 100)
            return "Password must be between 6 and 100 characters long";
        if (!HasMinimumPasswordStrength(password))
            return "Password should contain at least one letter and one number";
        if (string.IsNullOrWhiteSpace(confirmPassword) || password != confirmPassword)
            return "Passwords do not match";
        if (!termsAccepted)
            return "You must agree to the Terms of Service and Privacy Policy";
        return null;
    }

    private static bool IsValidOrientation(string orientation)
        => new[] { "Female", "Male", "Both" }.Contains(orientation);

    private static bool IsValidEmail(string email)
    {
        try { return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase); }
        catch { return false; }
    }

    private static bool HasMinimumPasswordStrength(string password)
        => password.Any(char.IsLetter) && password.Any(char.IsDigit);

    // ── Social / other button handlers ──────────────────────────────

    private async void OnSpotifySignUp(object sender, EventArgs e)
    {
        try
        {
            await Browser.OpenAsync($"{API_BASE_URL}/login", BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unable to open Spotify sign up: {ex.Message}", "OK");
        }
    }

    private async void OnGoogleSignUp(object sender, EventArgs e)
        => await DisplayAlert("Google Sign Up", "Google authentication coming soon!", "OK");

    private async void OnAppleSignUp(object sender, EventArgs e)
        => await DisplayAlert("Apple Sign Up", "Apple authentication coming soon!", "OK");

    private async void OnTermsClicked(object sender, EventArgs e)
        => await DisplayAlert("Terms of Service",
            "By using SpotiLove you agree to use the service respectfully.\nYou must be 18+.\nVisit our website for full terms.", "OK");

    private async void OnPrivacyClicked(object sender, EventArgs e)
        => await DisplayAlert("Privacy Policy",
            "We encrypt your data and never sell it.\nYou can delete your account at any time.\nVisit our website for the full policy.", "OK");

    // ── Response DTOs ────────────────────────────────────────────────

    public class RegisterResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public RegisteredUser? User { get; set; }
    }

    /// Minimal DTO matching exactly what the server returns in /auth/register
    public class RegisteredUser
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
        public string? Gender { get; set; }
        public string? SexualOrientation { get; set; }
        public string? Bio { get; set; }
    }
}
