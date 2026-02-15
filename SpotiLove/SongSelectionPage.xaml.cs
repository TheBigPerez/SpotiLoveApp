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
    private ObservableCollection<SongViewModel> _songs = new();
    private List<SongViewModel> _selectedSongs = new();
    private MediaElement? _currentPlayer;
    private SongViewModel? _currentlyPlayingSong;

    public SongSelectionPage(Guid userId, List<string> selectedArtists)
    {
        InitializeComponent();
        _userId = userId;
        _selectedArtists = selectedArtists;
        _httpClient = new HttpClient();
        SongsCollection.ItemsSource = _songs;

        _ = LoadSongsFromArtists();
    }

    private async Task LoadSongsFromArtists()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            foreach (var artistName in _selectedArtists)
            {
                Console.WriteLine($" Fetching tracks for artist: {artistName}");

                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/spotify/artist-top-tracks?artistName={Uri.EscapeDataString(artistName)}&limit=5"
                );

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response JSON: {json.Substring(0, Math.Min(200, json.Length))}...");

                    var songs = JsonSerializer.Deserialize<List<SpotifySong>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (songs != null)
                    {
                        Console.WriteLine($" Received {songs.Count} songs for {artistName}");

                        foreach (var song in songs)
                        {
                            Console.WriteLine($"   🎵 {song.Title}");
                            Console.WriteLine($"      Spotify: {song.PreviewUrl ?? "null"}");
                            Console.WriteLine($"      Deezer: {song.DeezerPreviewUrl ?? "null"}");

                            _songs.Add(new SongViewModel
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
                else
                {
                    Console.WriteLine($" Failed to fetch tracks for {artistName}: {response.StatusCode}");
                }

                await Task.Delay(100);
            }

            Console.WriteLine($" Total songs loaded: {_songs.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Error loading songs: {ex.Message}");
            await DisplayAlert("Error", $"Failed to load songs: {ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private void OnSongTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SongViewModel song)
            return;

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

    private void StopCurrentSong()
    {
        if (_currentPlayer != null)
        {
            _currentPlayer.Stop();
            _currentPlayer.Handler?.DisconnectHandler();
            _currentPlayer = null;
        }

        if (_currentlyPlayingSong != null)
        {
            _currentlyPlayingSong.IsPlaying = false;
            _currentlyPlayingSong.PlayButtonText = "▶";
            _currentlyPlayingSong = null;
        }
    }

    private void UpdateUI()
    {
        SelectedCountLabel.Text = $"{_selectedSongs.Count} selected";
        NextButton.IsEnabled = _selectedSongs.Count >= 5;
        NextButton.BackgroundColor = _selectedSongs.Count >= 5
            ? Color.FromArgb("#1db954")
            : Color.FromArgb("#535353");
    }

    private async void OnNextClicked(object sender, EventArgs e)
    {
        // Stop any playing music before navigating
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

            // Get genres from artists
            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/spotify/genres-from-artists?artists={Uri.EscapeDataString(string.Join(",", _selectedArtists))}"
            );

            List<string> genres = new();
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                genres = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }

            var finalGenres = genres
                .GroupBy(g => g.ToLower())
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();

            if (finalGenres.Count < 3)
            {
                finalGenres = genres.Take(3).ToList();
            }

            var songStrings = _selectedSongs.Select(s => $"{s.Title} by {s.Artist}").ToList();

            var profile = new
            {
                userId = _userId,
                artists = string.Join(", ", _selectedArtists),
                songs = string.Join(", ", songStrings),
                genres = string.Join(", ", finalGenres)
            };

            var content = new StringContent(
                JsonSerializer.Serialize(profile),
                Encoding.UTF8,
                "application/json"
            );

            var saveResponse = await _httpClient.PostAsync(
                $"{_apiBaseUrl}/users/{_userId}/profile",
                content
            );

            if (saveResponse.IsSuccessStatusCode)
            {
                await DisplayAlert("Success",
                    "Your music profile has been created automatically!", "OK");
                Application.Current.MainPage = new NavigationPage(new MainPage());
            }
            else
            {
                var error = await saveResponse.Content.ReadAsStringAsync();
                await DisplayAlert("Error", error, "OK");
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopCurrentSong();
    }

    private async void OnPlayButtonTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SongViewModel song)
            return;

        try
        {
            // If already playing this song, stop it
            if (song.IsPlaying)
            {
                StopCurrentSong();
                return;
            }

            // Stop any currently playing song
            StopCurrentSong();

            // 🔍 Debug logging
            Console.WriteLine($"🎵 Attempting playback for: {song.Title}");
            Console.WriteLine($"   Spotify Preview: {song.PreviewUrl ?? "null"}");
            Console.WriteLine($"   Deezer Preview: {song.DeezerPreviewUrl ?? "null"}");

            // 1️⃣ Try Spotify preview first
            if (!string.IsNullOrEmpty(song.PreviewUrl))
            {
                Console.WriteLine($"▶️ Playing Spotify preview: {song.Title}");
                await PlayPreview(song, song.PreviewUrl, "Spotify");
                return;
            }

            // 2️⃣ Try Deezer preview
            if (!string.IsNullOrEmpty(song.DeezerPreviewUrl))
            {
                Console.WriteLine($"▶️ Playing Deezer preview: {song.Title}");
                await PlayPreview(song, song.DeezerPreviewUrl, "Deezer");
                return;
            }

            // 3️⃣ No previews available - ask user what to do
            Console.WriteLine($"No previews available for: {song.Title}");
            var action = await DisplayActionSheet(
                "No Preview Available",
                "Cancel",
                null,
                "Search on YouTube",
                "Open in Spotify"
            );

            if (action == "Search on YouTube")
            {
                await SearchOnYouTube(song);
            }
            else if (action == "Open in Spotify")
            {
                await OpenInSpotify(song);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Playback error: {ex.Message}");
            await DisplayAlert("Error", $"Playback failed: {ex.Message}", "OK");
        }
    }

    private async Task PlayPreview(SongViewModel song, string url, string source)
    {
        try
        {
            Console.WriteLine($"🎧 Creating MediaElement for {source}: {url}");

            _currentPlayer = new MediaElement
            {
                Source = MediaSource.FromUri(url),
                ShouldAutoPlay = true,
                Volume = 0.8
            };

            _currentPlayer.MediaOpened += (s, args) =>
            {
                Console.WriteLine($" {source} preview opened successfully");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    song.IsPlaying = true;
                    song.PlayButtonText = "⏸";
                    _currentlyPlayingSong = song;
                });
            };

            _currentPlayer.MediaEnded += (s, args) =>
            {
                Console.WriteLine($"⏹️ {source} preview ended");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    song.IsPlaying = false;
                    song.PlayButtonText = "▶";
                    _currentlyPlayingSong = null;
                });
            };

            _currentPlayer.MediaFailed += async (s, args) =>
            {
                Console.WriteLine($" {source} preview failed: {args.ErrorMessage}");

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    song.IsPlaying = false;
                    song.PlayButtonText = "▶";

                    var tryYouTube = await DisplayAlert(
                        $"{source} Preview Failed",
                        $"Could not play {source} preview. Search on YouTube instead?",
                        "Yes",
                        "No"
                    );

                    if (tryYouTube)
                    {
                        await SearchOnYouTube(song);
                    }
                });
            };

            song.IsPlaying = true;
            song.PlayButtonText = "⏸";
            _currentlyPlayingSong = song;
            _currentPlayer.Play();

            Console.WriteLine($"▶️ Started playing {source} preview");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Exception playing {source}: {ex.Message}");
            throw;
        }
    }

    private async Task SearchOnYouTube(SongViewModel song)
    {
        try
        {
            var query = Uri.EscapeDataString($"{song.Title} {song.Artist} official audio");
            var youtubeUrl = $"https://www.youtube.com/results?search_query={query}";

            var opened = await Launcher.OpenAsync(youtubeUrl);

            if (opened)
            {
                Console.WriteLine($"🔍 Opened YouTube search: {song.Title}");
            }
            else
            {
                await DisplayAlert("Error", "Could not open YouTube", "OK");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Failed to open YouTube: {ex.Message}");
            await DisplayAlert("Error", $"Could not open YouTube: {ex.Message}", "OK");
        }
    }

    private async Task OpenInSpotify(SongViewModel song)
    {
        try
        {
            // Build Spotify search URL as fallback
            var searchQuery = Uri.EscapeDataString($"{song.Title} {song.Artist}");
            var spotifySearchUrl = $"https://open.spotify.com/search/{searchQuery}";

            var opened = await Launcher.OpenAsync(spotifySearchUrl);

            if (opened)
            {
                Console.WriteLine($" Opened Spotify search: {song.Title}");
            }
            else
            {
                await DisplayAlert("Error", "Could not open Spotify", "OK");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Failed to open Spotify: {ex.Message}");
            await DisplayAlert("Error", $"Could not open Spotify: {ex.Message}", "OK");
        }
    }
}

public class SongViewModel : BindableObject
{
    private string _title = "";
    private string _artist = "";
    private string? _previewUrl;
    private string? _spotifyUri;
    private string? _spotifyUrl;
    private bool _isSelected;
    private bool _isPlaying;
    private Color _borderColor = Colors.Transparent;
    private string _playButtonText = "▶";
    private string? _deezerPreviewUrl;

    public string? DeezerPreviewUrl
    {
        get => _deezerPreviewUrl;
        set { _deezerPreviewUrl = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Artist
    {
        get => _artist;
        set { _artist = value; OnPropertyChanged(); }
    }

    public string? PreviewUrl
    {
        get => _previewUrl;
        set { _previewUrl = value; OnPropertyChanged(); }
    }

    public string? SpotifyUri
    {
        get => _spotifyUri;
        set { _spotifyUri = value; OnPropertyChanged(); }
    }

    public string? SpotifyUrl
    {
        get => _spotifyUrl;
        set { _spotifyUrl = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; OnPropertyChanged(); }
    }

    public string PlayButtonText
    {
        get => _playButtonText;
        set { _playButtonText = value; OnPropertyChanged(); }
    }
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