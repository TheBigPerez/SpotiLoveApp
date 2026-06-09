using Microsoft.Maui;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace SpotiLove;

public partial class ArtistSelectionPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private static readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";
    private readonly Guid _userId;
    private ObservableCollection<ArtistViewModel> _artists = new();
    private List<ArtistViewModel> _selectedArtists = new();

    public ArtistSelectionPage()
    {
        InitializeComponent();
        _userId = UserData.Current.Id;
        _httpClient = new HttpClient();
        ArtistsCollection.ItemsSource = _artists;

        // Load default popular artists on startup
        _ = LoadPopularArtists();
    }

    // =========================================================
    //   Load Popular Artists
    // =========================================================
    private async Task LoadPopularArtists()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/popular-artists?limit=20");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var artists = JsonSerializer.Deserialize<List<SpotifyArtist>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _artists.Clear();
                foreach (var artist in artists ?? new())
                {
                    var imageUrl = artist.ImageUrl;
                    System.Diagnostics.Debug.WriteLine($"[Artist] {artist.Name} -> ImageUrl: {imageUrl}");

                    _artists.Add(new ArtistViewModel
                    {
                        Name = artist.Name,
                        ImageUrl = ProxiedImageUrl(artist.ImageUrl),
                        IsSelected = false,
                        BorderColor = Colors.Transparent
                    });
                }
            }
            else
            {
                await DisplayAlert("Error", "Failed to load popular artists.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load artists: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
    public static string ProxiedImageUrl(string? originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
            return "default_user.png";

        return $"{_apiBaseUrl}/proxy-image?url={Uri.EscapeDataString(originalUrl)}";
    }

    //  Search Artists
    private async void OnSearchArtists(object sender, EventArgs e)
    {
        var query = ArtistSearchBar.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            // if search cleared → reload popular artists
            await LoadPopularArtists();
            return;
        }

        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/spotify/search-artists?query={Uri.EscapeDataString(query)}&limit=20"
            );

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var artists = JsonSerializer.Deserialize<List<SpotifyArtist>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _artists.Clear();
                foreach (var artist in artists ?? new())
                {
                    var imageUrl = artist.ImageUrl;
                    _artists.Add(new ArtistViewModel
                    {
                        Name = artist.Name,
                        ImageUrl = ProxiedImageUrl(artist.ImageUrl),
                        IsSelected = _selectedArtists.Any(a => a.Name == artist.Name),
                        BorderColor = _selectedArtists.Any(a => a.Name == artist.Name)
                            ? Colors.Green
                            : Colors.Transparent
                    });
                }
            }
            else
            {
                await DisplayAlert("Error", "Search request failed.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Search failed: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    //  Artist Selection Logic
    private void OnArtistTapped(object sender, EventArgs e)
    {
        if (sender is not Frame frame || frame.BindingContext is not ArtistViewModel artist)
            return;

        artist.IsSelected = !artist.IsSelected;
        artist.BorderColor = artist.IsSelected ? Colors.Green : Colors.Transparent;

        if (artist.IsSelected)
        {
            if (!_selectedArtists.Any(a => a.Name == artist.Name))
                _selectedArtists.Add(artist);
        }
        else
        {
            _selectedArtists.RemoveAll(a => a.Name == artist.Name);
        }

        UpdateUI();
    }

    // 🔄 Update UI
    private void UpdateUI()
    {
        SelectedCountLabel.Text = $"{_selectedArtists.Count} selected";
        NextButton.IsEnabled = _selectedArtists.Count >= 3;
        NextButton.BackgroundColor = _selectedArtists.Count >= 3
            ? Color.FromArgb("#1db954")
            : Color.FromArgb("#535353");
    }

    //  Continue Button
    private async void OnNextClicked(object sender, EventArgs e)
    {
        if (_selectedArtists.Count < 3)
        {
            await DisplayAlert("Not Enough Artists", "Please select at least 3 artists", "OK");
            return;
        }

        // Navigate to next page
        await Navigation.PushAsync(new SongSelectionPage(_userId, _selectedArtists.Select(a => a.Name).ToList()));
    }
}

// =========================================================
//  View Models & DTOs
// =========================================================
public class ArtistViewModel : BindableObject
{
    private string _name = "";
    private string _imageUrl = "";
    private bool _isSelected;
    private Color _borderColor = Colors.Transparent;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string ImageUrl
    {
        get => _imageUrl;
        set { _imageUrl = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; OnPropertyChanged(); }
    }
}

public class SpotifyArtist
{
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
}
