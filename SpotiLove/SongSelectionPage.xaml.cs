using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Maui.Views;

namespace SpotiLove;

public partial class SongSelectionPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";
    private readonly Guid _userId;
    private readonly List<string> _selectedArtists;

    private List<SongViewModel> _allSongs = new();
    private ObservableCollection<SongViewModel> _displayedSongs = new();
    private List<SongViewModel> _selectedSongs = new();
    private SongViewModel? _currentlyPlayingSong;

    public SongSelectionPage(Guid userId, List<string> selectedArtists)
    {
        InitializeComponent();
        _userId = userId;
        _selectedArtists = selectedArtists;
        _httpClient = new HttpClient();
        SongsCollection.ItemsSource = _displayedSongs;

        NowPlayingBar.IsVisible = false;

        _ = LoadSongsFromArtists();
        AudioPlayer.MediaEnded += (s, e) =>
      MainThread.BeginInvokeOnMainThread(StopCurrentSong);
        AudioPlayer.MediaFailed += (s, e) =>
            MainThread.BeginInvokeOnMainThread(() => {
                StopCurrentSong();
                _ = DisplayAlert("Playback Failed", "Could not play preview.", "OK");
            });
    }

    // =========================================================
    // Audio — single reusable MediaElement in visual tree
    // =========================================================



    private void StopCurrentSong()
    {
        AudioPlayer.Stop();
        AudioPlayer.Source = null;

        if (_currentlyPlayingSong != null)
        {
            _currentlyPlayingSong.IsPlaying = false;
            _currentlyPlayingSong.PlayButtonText = "▶";
            _currentlyPlayingSong = null;
        }

        NowPlayingBar.IsVisible = false;
        NextButton.IsVisible = true;
    }

    private async void OnPlayButtonTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SongViewModel song) return;

        if (song.IsPlaying) { StopCurrentSong(); return; }

        StopCurrentSong();

        string? previewUrl = song.DeezerPreviewUrl ?? song.PreviewUrl;

        if (string.IsNullOrEmpty(previewUrl))
        {
            bool openYT = await DisplayAlert(
                "No Preview Available",
                $"No preview for \"{song.Title}\". Open YouTube?",
                "Open YouTube", "Cancel");
            if (openYT) await SearchOnYouTube(song);
            return;
        }

        try
        {
            AudioPlayer.Source = MediaSource.FromUri(previewUrl);
            AudioPlayer.Play();

            song.IsPlaying = true;
            song.PlayButtonText = "⏸";
            _currentlyPlayingSong = song;

            NowPlayingTitle.Text = song.Title;
            NowPlayingArtist.Text = song.Artist;
            NowPlayingBar.IsVisible = true;
            NextButton.IsVisible = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Playback error: {ex.Message}");
            await DisplayAlert("Error", "Playback failed. Try another song.", "OK");
        }
    }

    private void OnStopButtonClicked(object sender, EventArgs e)
    {
        StopCurrentSong();
    }

    // =========================================================
    // Load Songs
    // =========================================================

    private async Task LoadSongsFromArtists()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            _allSongs.Clear();
            _displayedSongs.Clear();

            foreach (var artistName in _selectedArtists)
            {
                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/spotify/artist-top-tracks?artistName={Uri.EscapeDataString(artistName)}&limit=5"
                );

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var songs = JsonSerializer.Deserialize<List<SpotifySong>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (songs != null)
                    {
                        foreach (var song in songs)
                        {
                            _allSongs.Add(new SongViewModel
                            {
                                Title = song.Title,
                                Artist = song.Artist,
                                PreviewUrl = song.PreviewUrl,
                                DeezerPreviewUrl = song.DeezerPreviewUrl,
                                IsSelected = false,
                                IsPlaying = false,
                                BorderColor = Colors.Transparent,
                                PlayButtonText = "▶"
                            });
                        }
                    }
                }

                await Task.Delay(100);
            }

            ApplyFilter(null);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load songs: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    // =========================================================
    // Search / Filter
    // =========================================================

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(e.NewTextValue);
    }

    private void ApplyFilter(string? searchText)
    {
        _displayedSongs.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _allSongs
            : _allSongs.Where(s =>
                s.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(searchText, StringComparison.OrdinalIgnoreCase)
              ).ToList();

        foreach (var song in filtered)
            _displayedSongs.Add(song);

        NoResultsView.IsVisible = !filtered.Any() && _allSongs.Any();
    }

    // =========================================================
    // Song Selection
    // =========================================================

    private void OnSongTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SongViewModel song) return;

        song.IsSelected = !song.IsSelected;
        song.BorderColor = song.IsSelected ? Color.FromArgb("#1db954") : Colors.Transparent;

        if (song.IsSelected)
        {
            if (!_selectedSongs.Any(s => s.Title == song.Title && s.Artist == song.Artist))
                _selectedSongs.Add(song);
        }
        else
        {
            _selectedSongs.RemoveAll(s => s.Title == song.Title && s.Artist == song.Artist);
        }

        UpdateUI();
    }

    private void UpdateUI()
    {
        SelectedCountLabel.Text = $"{_selectedSongs.Count} selected";
        NextButton.IsEnabled = _selectedSongs.Count >= 5;
        NextButton.BackgroundColor = _selectedSongs.Count >= 5
            ? Color.FromArgb("#1db954")
            : Color.FromArgb("#535353");
    }

    // =========================================================
    // Continue / Save
    // =========================================================

    private async void OnNextClicked(object sender, EventArgs e)
    {
        StopCurrentSong();

        if (_selectedSongs.Count < 5)
        {
            await DisplayAlert("Not Enough Songs", "Please select at least 5 songs", "OK");
            return;
        }

        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            var genreResponse = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/spotify/genres-from-artists?artists={Uri.EscapeDataString(string.Join(",", _selectedArtists))}"
            );

            List<string> genres = new();
            if (genreResponse.IsSuccessStatusCode)
            {
                var json = await genreResponse.Content.ReadAsStringAsync();
                genres = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }

            var finalGenres = genres
                .GroupBy(g => g.ToLower())
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();

            if (finalGenres.Count < 3) finalGenres = genres.Take(3).ToList();

            var songStrings = _selectedSongs.Select(s => $"{s.Title} by {s.Artist}").ToList();

            var profile = new
            {
                userId = _userId,
                artists = string.Join(", ", _selectedArtists),
                songs = string.Join(", ", songStrings),
                genres = string.Join(", ", finalGenres)
            };

            var content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json");
            var saveResponse = await _httpClient.PostAsync($"{_apiBaseUrl}/users/{_userId}/profile", content);

            if (saveResponse.IsSuccessStatusCode)
            {
                await DisplayAlert("Success", "Your music profile has been created!", "Let's Go!");
                NavigateToMain();
            }
            else
            {
                var error = await saveResponse.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Failed to save profile: {error}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private static void NavigateToMain()
    {
#pragma warning disable CS0618 // Application.MainPage is still the simplest cross-platform nav approach
        Application.Current!.MainPage = new NavigationPage(new MainPage());
#pragma warning restore CS0618
    }

    private async Task SearchOnYouTube(SongViewModel song)
    {
        try
        {
            var query = Uri.EscapeDataString($"{song.Title} {song.Artist} official audio");
            await Launcher.OpenAsync($"https://www.youtube.com/results?search_query={query}");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not open YouTube: {ex.Message}", "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopCurrentSong();
    }
}

// =========================================================
// View Models & DTOs
// =========================================================
public class SongViewModel : BindableObject
{
    private string _title = "";
    private string _artist = "";
    private string? _previewUrl;
    private string? _deezerPreviewUrl;
    private bool _isSelected;
    private bool _isPlaying;
    private Color _borderColor = Colors.Transparent;
    private string _playButtonText = "▶";

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Artist { get => _artist; set { _artist = value; OnPropertyChanged(); } }
    public string? PreviewUrl { get => _previewUrl; set { _previewUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoPreview)); } }
    public string? DeezerPreviewUrl { get => _deezerPreviewUrl; set { _deezerPreviewUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoPreview)); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }
    public Color BorderColor { get => _borderColor; set { _borderColor = value; OnPropertyChanged(); } }
    public string PlayButtonText { get => _playButtonText; set { _playButtonText = value; OnPropertyChanged(); } }

    public bool HasNoPreview =>
        string.IsNullOrEmpty(PreviewUrl) && string.IsNullOrEmpty(DeezerPreviewUrl);
}

public class SpotifySong
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? PreviewUrl { get; set; }
    public string? SpotifyUri { get; set; }
    public string? SpotifyUrl { get; set; }
    public string? DeezerPreviewUrl { get; set; }
}
