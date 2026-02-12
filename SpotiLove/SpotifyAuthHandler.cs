using System.Web;
using System.Diagnostics;
using System.Text.Json;

namespace SpotiLove;

public class SpotifyAuthHandler
{
    public static async Task HandleSpotifyCallback(string uri)
    {
        try
        {
            Debug.WriteLine("======================================");
            Debug.WriteLine($"   Processing Spotify callback: {uri}");
            Debug.WriteLine("======================================");

            var parsedUri = new Uri(uri);
            var queryParams = HttpUtility.ParseQueryString(parsedUri.Query);

            var token = queryParams["token"];
            var userIdStr = queryParams["userId"];
            var isNewUserStr = queryParams["isNewUser"];
            var name = queryParams["name"];

            Debug.WriteLine($"Parsed parameters:");
            Debug.WriteLine($"Token: {(token != null ? $"{token.Substring(0, Math.Min(8, token.Length))}..." : "null")}");
            Debug.WriteLine($"UserId: {userIdStr}");
            Debug.WriteLine($"IsNew: {isNewUserStr}");
            Debug.WriteLine($"Name: {name}");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userIdStr))
            {
                Debug.WriteLine(" Missing token or userId in callback");
                await SafeDisplayAlert("Error", "Invalid authentication response");
                return;
            }

            if (!Guid.TryParse(userIdStr, out Guid userId))
            {
                Debug.WriteLine($" Invalid userId format: {userIdStr}");
                await SafeDisplayAlert("Error", "Invalid user ID in response");
                return;
            }

            bool isNewUser = bool.Parse(isNewUserStr ?? "false");

            Debug.WriteLine($" Valid parameters parsed - UserId: {userId}");

            // Store authentication data
            await SecureStorage.SetAsync("auth_token", token);
            await SecureStorage.SetAsync("user_id", userId.ToString());
            Debug.WriteLine(" Saved auth token and user ID to secure storage");

            // Fetch full user profile from API
            Debug.WriteLine(" Fetching user profile from API...");
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://spotilove.danielnaz.com");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync($"/users/{userId}");
            Debug.WriteLine($" API Response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($" Response length: {content.Length} characters");

                var userResponse = JsonSerializer.Deserialize<ApiUserResponse>(
                    content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (userResponse?.User != null)
                {
                    Debug.WriteLine($" User data deserialized: {userResponse.User.Name}");

                    // Set global user data
                    UserData.Current = new UserData
                    {
                        Id = userResponse.User.Id,
                        Name = userResponse.User.Name,
                        Email = userResponse.User.Email,
                        Age = userResponse.User.Age
                    };

                    await SecureStorage.SetAsync("user_name", userResponse.User.Name ?? "");
                    await SecureStorage.SetAsync("user_email", userResponse.User.Email ?? "");

                    Debug.WriteLine($" UserData.Current set:");
                    Debug.WriteLine($"   ID: {UserData.Current.Id}");
                    Debug.WriteLine($"   Name: {UserData.Current.Name}");
                    Debug.WriteLine($"   Email: {UserData.Current.Email}");
                    Debug.WriteLine($"   Age: {UserData.Current.Age}");

                    // Check if profile is complete
                    bool isProfileIncomplete = userResponse.User.Age == 0 ||
                                              string.IsNullOrEmpty(userResponse.User.Gender) ||
                                              string.IsNullOrEmpty(userResponse.User.SexualOrientation);

                    bool isMusicProfileEmpty = userResponse.User.MusicProfile == null ||
                                              (userResponse.User.MusicProfile.FavoriteArtists?.Count == 0 &&
                                               userResponse.User.MusicProfile.FavoriteGenres?.Count == 0 &&
                                               userResponse.User.MusicProfile.FavoriteSongs?.Count == 0);

                    Debug.WriteLine($" Profile status:");
                    Debug.WriteLine($"   Basic profile incomplete: {isProfileIncomplete}");
                    Debug.WriteLine($"   Music profile empty: {isMusicProfileEmpty}");

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (isProfileIncomplete)
                        {
                            Debug.WriteLine(" Navigating to CompleteProfilePage...");
                            await SafeDisplayAlert(
                                "Welcome to SpotiLove!",
                                $"Hi {userResponse.User.Name}! Let's set up your profile."
                            );
                            await SafeNavigate(async () =>
                            {
                                var mainPage = Application.Current?.MainPage;
                                if (mainPage != null)
                                {
                                    await mainPage.Navigation.PushAsync(
                                        new CompleteProfilePage(userResponse.User.Id, userResponse.User.Name ?? "")
                                    );
                                }
                            });
                        }
                        else if (isMusicProfileEmpty)
                        {
                            Debug.WriteLine(" Navigating to ArtistSelectionPage...");
                            await SafeDisplayAlert(
                                "Set Up Your Music Profile",
                                "Let's find your music taste!"
                            );
                            await SafeNavigate(async () =>
                            {
                                var mainPage = Application.Current?.MainPage;
                                if (mainPage != null)
                                {
                                    await mainPage.Navigation.PushAsync(new ArtistSelectionPage());
                                }
                            });
                        }
                        else
                        {
                            Debug.WriteLine(" Navigating to MainPage...");
                            string message = $"Welcome back, {userResponse.User.Name}!";
                            await SafeDisplayAlert("Success", message);
                            await SafeNavigate(async () =>
                            {
                                if (Shell.Current != null)
                                {
                                    await Shell.Current.GoToAsync("//MainPage");
                                }
                                else
                                {
                                    Application.Current.MainPage = new AppShell();
                                }
                            });
                        }
                    });

                    Debug.WriteLine(" Navigation complete");
                }
                else
                {
                    Debug.WriteLine(" User data was null after deserialization");
                    await SafeDisplayAlert("Error", "Failed to load user profile");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($" API error: {response.StatusCode}");
                Debug.WriteLine($" Error content: {errorContent}");
                await SafeDisplayAlert("Error", "Failed to fetch user profile from server");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Exception in HandleSpotifyCallback: {ex.Message}");
            Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            await SafeDisplayAlert("Error", $"Failed to complete Spotify authentication: {ex.Message}");
        }
    }

    //  Safe method to display alerts without depending on Shell.Current
    private static async Task SafeDisplayAlert(string title, string message)
    {
        try
        {
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert(title, message, "OK");
            }
            else if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert(title, message, "OK");
            }
            else
            {
                Debug.WriteLine($"Cannot display alert - no main page available: {title}: {message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Failed to display alert: {ex.Message}");
        }
    }

    // Safe method to navigate without depending on Shell.Current
    private static async Task SafeNavigate(Func<Task> navigationAction)
    {
        try
        {
            await navigationAction();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Navigation error: {ex.Message}");
            // Fallback: Try to set a new AppShell as MainPage
            try
            {
                Application.Current.MainPage = new AppShell();
            }
            catch (Exception fallbackEx)
            {
                Debug.WriteLine($" Fallback navigation also failed: {fallbackEx.Message}");
            }
        }
    }

    private class ApiUserResponse
    {
        public UserDto? User { get; set; }
    }
}