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
        _httpClient = new HttpClient { BaseAddress = new Uri(API_BASE_URL) };

        // Add character count handler for bio
        BioEditor.TextChanged += OnBioTextChanged;
    }

    private void OnBioTextChanged(object sender, TextChangedEventArgs e)
    {
        if (BioCharCountLabel != null)
        {
            int currentLength = e.NewTextValue?.Length ?? 0;
            BioCharCountLabel.Text = $"{currentLength}/500 characters";

            // Change color when approaching limit
            if (currentLength > 450)
            {
                BioCharCountLabel.TextColor = Color.FromArgb("#ff4444");
            }
            else if (currentLength > 400)
            {
                BioCharCountLabel.TextColor = Color.FromArgb("#ffaa00");
            }
            else
            {
                BioCharCountLabel.TextColor = Color.FromArgb("#888888");
            }
        }
    }

    private async void OnGoToLogin(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//Login");
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//Login");
    }

    private async void OnCreateAccount(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        var email = EmailEntry.Text?.Trim();
        var ageText = AgeEntry.Text?.Trim();
        var gender = GenderPicker.SelectedItem?.ToString();
        var sexualOrientation = SexualOrientationPicker.SelectedItem?.ToString();
        var bio = BioEditor.Text?.Trim(); // Get bio text
        var password = PasswordEntry.Text;
        var confirmPassword = ConfirmPasswordEntry.Text;
        var termsAccepted = TermsCheckBox.IsChecked;

        var validationError = ValidateForm(name, email, ageText, gender, sexualOrientation, password, confirmPassword, termsAccepted);
        if (validationError != null)
        {
            await DisplayAlert("Validation Error", validationError, "OK");
            return;
        }

        int age = int.Parse(ageText!);

        try
        {
            // Show Loading Indicator
            var loadingPage = new ContentPage
            {
                Content = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 20,
                    Children =
                    {
                        new ActivityIndicator
                        {
                            IsRunning = true,
                            Color = Color.FromArgb("#1db954"),
                            WidthRequest = 50,
                            HeightRequest = 50
                        },
                        new Label
                        {
                            Text = "Creating your account...",
                            TextColor = Colors.White,
                            FontSize = 16
                        }
                    }
                },
                BackgroundColor = Color.FromArgb("#121212")
            };

            await Navigation.PushModalAsync(loadingPage, false);

            // Prepare API Call (include bio)
            var registerData = new
            {
                Name = name,
                Email = email,
                Password = password,
                Age = age,
                Gender = gender,
                SexualOrientation = sexualOrientation,
                Bio = bio, // Include bio in registration
                ProfileImage = _selectedImageBase64
            };

            var json = JsonSerializer.Serialize(registerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/auth/register", content);

            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync(false);
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var registerResponse = JsonSerializer.Deserialize<RegisterResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (registerResponse?.Success == true && registerResponse.User != null)
                {
                    await SecureStorage.SetAsync("user_id", registerResponse.User.Id.ToString());
                    await SecureStorage.SetAsync("user_email", registerResponse.User.Email ?? "");
                    await SecureStorage.SetAsync("user_name", registerResponse.User.Name ?? "");
                    if (!string.IsNullOrEmpty(registerResponse.Token))
                    {
                        await SecureStorage.SetAsync("auth_token", registerResponse.Token);
                    }

                    UserData.Current = registerResponse.User;

                    await DisplayAlert("Success",
                        $"Welcome to SpotiLove, {registerResponse.User.Name}! Your account has been created.",
                        "Get Started");

                    await Navigation.PushAsync(new ArtistSelectionPage());
                }
                else
                {
                    await DisplayAlert("Registration Failed",
                        registerResponse?.Message ?? "Unable to create account",
                        "OK");
                }
            }
            else
            {
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    await DisplayAlert("Registration Failed",
                        errorResponse?.Message ?? "Server error occurred. Please try again.",
                        "OK");
                }
                catch
                {
                    await DisplayAlert("Registration Failed",
                        "An unknown error occurred during registration. Please try again.",
                        "OK");
                }
            }
        }
        catch (HttpRequestException)
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync(false);
            }
            await DisplayAlert("Connection Error",
                "Unable to connect to the server. Please check your internet connection and try again.",
                "OK");
        }
        catch (Exception ex)
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync(false);
            }
            await DisplayAlert("Error",
                $"An unexpected error occurred: {ex.Message}",
                "OK");
        }
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

            if (result == null)
                return;

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
                                 string? sexualOrientation, string? password, string? confirmPassword, bool termsAccepted)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 50)
            return "Name must be between 2 and 50 characters long";

        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            return "Please enter a valid email address";

        if (string.IsNullOrWhiteSpace(ageText) || !int.TryParse(ageText, out int age) || age < 18 || age > 120)
            return "You must be a valid age between 18 and 120";

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
            return "You must agree to the Terms of Service and Privacy Policy to continue";

        return null;
    }

    private bool IsValidOrientation(string orientation)
    {
        var validOptions = new[] { "Female", "Male", "Both" };
        return validOptions.Contains(orientation);
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool HasMinimumPasswordStrength(string password)
    {
        bool hasLetter = password.Any(char.IsLetter);
        bool hasDigit = password.Any(char.IsDigit);
        return hasLetter && hasDigit;
    }

    private async void OnSpotifySignUp(object sender, EventArgs e)
    {
        try
        {
            var spotifyLoginUrl = $"{API_BASE_URL}/login";
            await Browser.OpenAsync(spotifyLoginUrl, BrowserLaunchMode.SystemPreferred);
            Thread.Sleep(3000);
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
            await DisplayAlert("Error", $"Unable to open Spotify sign up: {ex.Message}", "OK");
        }
    }

    private async void OnGoogleSignUp(object sender, EventArgs e)
    {
        await DisplayAlert("Google Sign Up", "Google authentication coming soon!", "OK");
    }

    private async void OnAppleSignUp(object sender, EventArgs e)
    {
        await DisplayAlert("Apple Sign Up", "Apple authentication coming soon!", "OK");
    }

    private async void OnTermsClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Terms of Service",
            "By using SpotiLove, you agree to our Terms of Service.\n\n" +
            "• You must be 18 years or older\n" +
            "• You agree to use the service respectfully\n" +
            "• Your account is personal and non-transferable\n" +
            "• We reserve the right to terminate accounts that violate our terms\n\n" +
            "For full terms, visit our website.",
            "OK");
    }

    private async void OnPrivacyClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Privacy Policy",
            "Your privacy is important to us.\n\n" +
            "• We encrypt your personal data\n" +
            "• We never sell your information\n" +
            "• You control your profile visibility\n" +
            "• You can delete your account anytime\n" +
            "• We use cookies to improve your experience\n\n" +
            "For full privacy policy, visit our website.",
            "OK");
    }

    public class RegisterResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public UserData? User { get; set; }
    }

    public class ErrorResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}