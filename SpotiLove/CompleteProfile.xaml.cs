using System.Text;
using System.Text.Json;

namespace SpotiLove;

public partial class CompleteProfilePage : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";
    private Guid _userId;
    private string _userName;

    public CompleteProfilePage()
    {
        InitializeComponent();
        _httpClient = new HttpClient();
        _userId = Guid.Empty;
        _userName = "User";
    }

    public CompleteProfilePage(Guid userId, string userName) : this()
    {
        _userId = userId;
        _userName = userName;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // If no userId was provided via constructor, try to get from UserData
        if (_userId == Guid.Empty && UserData.Current != null)
        {
            _userId = UserData.Current.Id;
            _userName = UserData.Current.Name ?? "User";
        }
    }

    private void OnBioTextChanged(object sender, TextChangedEventArgs e)
    {
        var length = BioEditor.Text?.Length ?? 0;
        BioCharCountLabel.Text = $"{length}/500 characters";
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        // Validate Age
        if (string.IsNullOrWhiteSpace(AgeEntry.Text))
        {
            await DisplayAlert("Required Field", "Please enter your age", "OK");
            return;
        }

        if (!int.TryParse(AgeEntry.Text, out int age) || age < 18 || age > 120)
        {
            await DisplayAlert("Invalid Age", "Please enter a valid age between 18 and 120", "OK");
            return;
        }

        // Validate Gender
        if (GenderPicker.SelectedIndex == -1)
        {
            await DisplayAlert("Required Field", "Please select your gender", "OK");
            return;
        }

        // Validate Sexual Orientation
        if (SexualOrientationPicker.SelectedIndex == -1)
        {
            await DisplayAlert("Required Field", "Please select who you're interested in", "OK");
            return;
        }

        try
        {
            ContinueButton.IsEnabled = false;
            ContinueButton.Text = "Updating profile...";

            var sexualOrientation = SexualOrientationPicker.SelectedItem.ToString() switch
            {
                "Men" => "Male",
                "Women" => "Female",
                "Everyone" => "Both",
                _ => "Both"
            };

            var profileUpdate = new
            {
                age = age,
                gender = GenderPicker.SelectedItem.ToString(),
                sexualOrientation = sexualOrientation,
                bio = string.IsNullOrWhiteSpace(BioEditor.Text) ? null : BioEditor.Text.Trim()
            };

            System.Diagnostics.Debug.WriteLine($"Sending profile update:");
            System.Diagnostics.Debug.WriteLine($"Age: {profileUpdate.age}");
            System.Diagnostics.Debug.WriteLine($"Gender: {profileUpdate.gender}");
            System.Diagnostics.Debug.WriteLine($"SexualOrientation: {profileUpdate.sexualOrientation}");

            var content = new StringContent(
                JsonSerializer.Serialize(profileUpdate),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PutAsync(
                $"{_apiBaseUrl}/users/{_userId}/basic-profile",
                content
            );

            var responseBody = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"Response Body: {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                //VERIFY the update worked by fetching the user again
                var verifyResponse = await _httpClient.GetAsync($"{_apiBaseUrl}/users/{_userId}");
                if (verifyResponse.IsSuccessStatusCode)
                {
                    var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Verified profile: {verifyJson}");
                }

                UserData.Current.Age = age;

                await DisplayAlert("Success", "Profile updated! Let's find your music taste.", "Continue");
                await Navigation.PushAsync(new ArtistSelectionPage());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Profile update failed!");
                await DisplayAlert("Error", $"Failed to update profile: {responseBody}", "OK");
                ContinueButton.IsEnabled = true;
                ContinueButton.Text = "Continue to Music Selection";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            ContinueButton.Text = "Continue to Music Selection";
        }
    }

    private async void OnSkipClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Skip Profile Setup?",
            "You can complete your profile later in settings, but it may affect match quality.",
            "Skip Anyway",
            "Go Back"
        );

        if (confirm)
        {
            await Navigation.PushAsync(new ArtistSelectionPage());
        }
    }
}