using System.Text.Json;
using System.Diagnostics;

namespace SpotiLove
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            //Initialize with AppShell immediately
            MainPage = new AppShell();

            // Register handler for query parameters (alternative to deep links)
            Routing.RegisterRoute("auth", typeof(Login));
        }

        protected override async void OnStart()
        {
            try
            {
                Debug.WriteLine(" App OnStart called");

                // Check for saved session
                var userId = await SecureStorage.GetAsync("user_id");
                if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out Guid id))
                {
                    var name = await SecureStorage.GetAsync("user_name");
                    var email = await SecureStorage.GetAsync("user_email");

                    UserData.Current = new UserData
                    {
                        Id = id,
                        Name = name,
                        Email = email
                    };

                    Debug.WriteLine($" Restored user session: Id={id}, Name={name}");
                    await ValidateUserProfile(id, name, "https://spotilove.danielnaz.com");
                }
                else
                {
                    Debug.WriteLine("No saved user session found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($" OnStart restore error: {ex.Message}");
            }
        }

        private async Task ValidateUserProfile(Guid userId, string? userName, string uriString)
        {
            try
            {
                Debug.WriteLine($"🔍 Validating profile completeness for user {userId}");

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(uriString);
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync($"/users/{userId}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($" User not found in database (Status: {response.StatusCode})");
                    await HandleInvalidUser();
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                var userResponse = JsonSerializer.Deserialize<UserProfileResponse>(
                    content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (userResponse?.User == null)
                {
                    Debug.WriteLine(" Failed to deserialize user data");
                    await HandleInvalidUser();
                    return;
                }

                var user = userResponse.User;
                UserData.Current.Age = user.Age;

                bool isProfileIncomplete = user.Age == 0 ||
                                          string.IsNullOrEmpty(user.Gender) ||
                                          string.IsNullOrEmpty(user.SexualOrientation);

                bool isMusicProfileEmpty = user.MusicProfile == null ||
                                           (user.MusicProfile.FavoriteArtists?.Count == 0 &&
                                            user.MusicProfile.FavoriteGenres?.Count == 0 &&
                                            user.MusicProfile.FavoriteSongs?.Count == 0);

                if (isProfileIncomplete)
                {
                    Debug.WriteLine("Basic profile incomplete");
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (MainPage?.Navigation != null)
                        {
                            await MainPage.Navigation.PushAsync(
                                new CompleteProfilePage(userId, userName ?? "User")
                            );
                        }
                    });
                }
                else if (isMusicProfileEmpty)
                {
                    Debug.WriteLine("Music profile empty");
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (MainPage != null)
                        {
                            await MainPage.DisplayAlert(
                                "Complete Your Profile",
                                "Let's set up your music preferences!",
                                "Continue"
                            );

                            if (MainPage.Navigation != null)
                            {
                                await MainPage.Navigation.PushAsync(new ArtistSelectionPage());
                            }
                        }
                    });
                }
                else
                {
                    Debug.WriteLine(" Profile complete");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($" Profile validation error: {ex.Message}");
            }
        }

        private async Task HandleInvalidUser()
        {
            Debug.WriteLine("Clearing invalid session");

            SecureStorage.Remove("user_id");
            SecureStorage.Remove("user_name");
            SecureStorage.Remove("user_email");
            SecureStorage.Remove("auth_token");

            UserData.Current = null;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (MainPage != null)
                {
                    await MainPage.DisplayAlert(
                        "Session Expired",
                        "Please log in again.",
                        "OK"
                    );
                }

                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//Login");
                }
                else
                {
                    MainPage = new AppShell();
                }
            });
        }

        //Handle Deep Links
        protected override async void OnAppLinkRequestReceived(Uri uri)
        {
            base.OnAppLinkRequestReceived(uri);

            Debug.WriteLine("=================================================");
            Debug.WriteLine($"🔗 DEEP LINK RECEIVED!");
            Debug.WriteLine($"   Full URI: {uri}");
            Debug.WriteLine($"   Scheme: {uri.Scheme}");
            Debug.WriteLine($"   Host: {uri.Host}");
            Debug.WriteLine($"   Path: {uri.AbsolutePath}");
            Debug.WriteLine($"   Query: {uri.Query}");
            Debug.WriteLine("=================================================");

            try
            {
                // Add delay to ensure UI is ready
                await Task.Delay(500);

                if (uri.Scheme.ToLower() == "spotilove" && uri.Host.ToLower() == "auth")
                {
                    if (uri.AbsolutePath.Contains("success") || uri.Query.Contains("token"))
                    {
                        Debug.WriteLine(" Success callback - processing...");
                        await SpotifyAuthHandler.HandleSpotifyCallback(uri.ToString());
                    }
                    else if (uri.AbsolutePath.Contains("error"))
                    {
                        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                        var errorMessage = queryParams["message"] ?? "Authentication failed";

                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await MainPage.DisplayAlert("Authentication Error", errorMessage, "OK");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($" Error handling deep link: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await MainPage.DisplayAlert("Error", $"Failed to process authentication: {ex.Message}", "OK");
                });
            }
        }
        // DTO for API response
        private class UserProfileResponse
        {
            public UserDto? User { get; set; }
        }
    }
}