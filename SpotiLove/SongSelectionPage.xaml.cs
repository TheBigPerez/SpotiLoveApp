// ============================================================
// REPLACE your existing SongSelectionPage.xaml.cs with this
// ============================================================

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

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
    private bool _isPaused = false;

    public SongSelectionPage(Guid userId, List<string> selectedArtists)
    {
        InitializeComponent();
        _userId = userId;
        _selectedArtists = selectedArtists;
        _httpClient = new HttpClient();
        SongsCollection.ItemsSource = _displayedSongs;
        NowPlayingBar.IsVisible = false;
        NextButton.IsVisible = true;

        WireUpPlayer();
        _ = InitializePlayerAsync();
        _ = LoadSongsFromArtists();
    }

    // ── Player setup ─────────────────────────────────────────

    private void WireUpPlayer()
    {
        SpotifyPlayer.PlayerReady += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Console.WriteLine("[SongSelection] Spotify player is ready!");
            });
        };

        SpotifyPlayer.PlaybackStateChanged += isPlaying =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_currentlyPlayingSong == null) return;

                if (!isPlaying && !_isPaused)
                {
                    // Track ended naturally
                    StopCurrentSong();
                }
            });
        };

        SpotifyPlayer.PlaybackError += errorMsg =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                Console.WriteLine($"[SongSelection] Playback error: {errorMsg}");

                // account_error = no Premium
                if (errorMsg?.Contains("account") == true)
                {
                    await DisplayAlert("Spotify Premium Required",
                        "Full playback requires Spotify Premium. Opening song in Spotify app instead.",
                        "OK");

                    if (_currentlyPlayingSong?.SpotifyUri != null)
                        await Launcher.OpenAsync(new Uri(_currentlyPlayingSong.SpotifyUri));
                }
                else
                {
                    await DisplayAlert("Playback Error", errorMsg ?? "Unknown error", "OK");
                }

                StopCurrentSong();
            });
        };
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            await SpotifyPlayer.InitializeAsync(_userId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SongSelection] Player init error: {ex.Message}");
        }
    }

    // ── Play / Pause / Stop ──────────────────────────────────

    private async void OnPlayButtonTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not SongViewModel song) return;

        // Tapped the currently playing song → pause/resume toggle
        if (_currentlyPlayingSong == song)
        {
            if (_isPaused)
            {
                await SpotifyPlayer.ResumeAsync();
                _isPaused = false;
                song.PlayButtonText = "⏸";
                song.PlayButtonColor = Color.FromArgb("#1db954");
                PauseResumeButton.Text = "⏸";
            }
            else
            {
                await SpotifyPlayer.PauseAsync();
                _isPaused = true;
                song.PlayButtonText = "▶";
                song.PlayButtonColor = Color.FromArgb("#535353");
                PauseResumeButton.Text = "▶";
            }
            return;
        }

        // Tapped a different song — stop current, start new
        StopCurrentSong();

        if (string.IsNullOrEmpty(song.SpotifyUri))
        {
            await DisplayAlert("Not Available", "No Spotify URI for this track.", "OK");
            return;
        }

        // Start playing
        song.PlayButtonText = "⏸";
        song.PlayButtonColor = Color.FromArgb("#1db954");
        song.IsPlaying = true;
        _currentlyPlayingSong = song;
        _isPaused = false;

        NowPlayingTitle.Text = song.Title;
        NowPlayingArtist.Text = song.Artist;
        NowPlayingBar.IsVisible = true;
        NextButton.IsVisible = false;
        PauseResumeButton.Text = "⏸";

        await SpotifyPlayer.PlayAsync(song.SpotifyUri);
    }

    private async void OnPauseResumeClicked(object sender, EventArgs e)
    {
        if (_currentlyPlayingSong == null) return;

        if (_isPaused)
        {
            await SpotifyPlayer.ResumeAsync();
            _isPaused = false;
            _currentlyPlayingSong.PlayButtonText = "⏸";
            _currentlyPlayingSong.PlayButtonColor = Color.FromArgb("#1db954");
            PauseResumeButton.Text = "⏸";
        }
        else
        {
            await SpotifyPlayer.PauseAsync();
            _isPaused = true;
            _currentlyPlayingSong.PlayButtonText = "▶";
            _currentlyPlayingSong.PlayButtonColor = Color.FromArgb("#535353");
            PauseResumeButton.Text = "▶";
        }
    }

    private async void OnStopButtonClicked(object sender, EventArgs e)
    {
        await SpotifyPlayer.StopAsync();
        StopCurrentSong();
    }

    private void StopCurrentSong()
    {
        if (_currentlyPlayingSong != null)
        {
            _currentlyPlayingSong.IsPlaying = false;
            _currentlyPlayingSong.PlayButtonText = "▶";
            _currentlyPlayingSong.PlayButtonColor = Color.FromArgb("#1db954");
            _currentlyPlayingSong = null;
        }
        _isPaused = false;
        NowPlayingBar.IsVisible = false;
        NextButton.IsVisible = true;
    }

    // ── Load songs from backend ──────────────────────────────

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
                    $"{_apiBaseUrl}/spotify/artist-top-tracks?artistName={Uri.EscapeDataString(artistName)}&limit=5");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var songs = JsonSerializer.Deserialize<List<SpotifySong>>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (songs != null)
                    {
                        foreach (var song in songs)
                        {
                            Console.WriteLine($"[Song] {song.Title} | URI: {song.SpotifyUri}");
                            _allSongs.Add(new SongViewModel
                            {
                                Title = song.Title,
                                Artist = song.Artist,
                                SpotifyUri = song.SpotifyUri,
                                SpotifyUrl = song.SpotifyUrl,
                                IsSelected = false,
                                IsPlaying = false,
                                BorderColor = Colors.Transparent,
                                PlayButtonText = "▶",
                                PlayButtonColor = Color.FromArgb("#1db954")
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

    // ── Search ───────────────────────────────────────────────

    // Replace OnSearchTextChanged:
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim();

        // If empty → restore the original artist songs
        if (string.IsNullOrWhiteSpace(query))
        {
            ApplyFilter(null);
            return;
        }

        // Debounce: only hit the API after the user stops typing for 600ms
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(600, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => _ = SearchSongsFromApi(query));
        }, token);
    }

    // Add this field at the top of the class (alongside the other fields):
    private CancellationTokenSource? _searchCts;

    // Add this new method:
    private async Task SearchSongsFromApi(string query)
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/spotify/search-songs?query={Uri.EscapeDataString(query)}&limit=20");

            if (!response.IsSuccessStatusCode)
            {
                ApplyFilter(query); // Fall back to local filter
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var songs = JsonSerializer.Deserialize<List<SpotifySong>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _displayedSongs.Clear();

            if (songs == null || songs.Count == 0)
            {
                NoResultsView.IsVisible = true;
                return;
            }

            NoResultsView.IsVisible = false;

            foreach (var song in songs)
            {
                // Preserve selection state if song is already chosen
                bool alreadySelected = _selectedSongs.Any(
                    s => s.Title == song.Title && s.Artist == song.Artist);

                _displayedSongs.Add(new SongViewModel
                {
                    Title = song.Title,
                    Artist = song.Artist,
                    SpotifyUri = song.SpotifyUri,
                    SpotifyUrl = song.SpotifyUrl,
                    IsSelected = alreadySelected,
                    IsPlaying = false,
                    BorderColor = alreadySelected ? Color.FromArgb("#1db954") : Colors.Transparent,
                    PlayButtonText = "▶",
                    PlayButtonColor = Color.FromArgb("#1db954")
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Song search error: {ex.Message}");
            ApplyFilter(query); // Fall back gracefully
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private void ApplyFilter(string? searchText)
    {
        _displayedSongs.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _allSongs
            : _allSongs.Where(s =>
                s.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var song in filtered)
            _displayedSongs.Add(song);

        NoResultsView.IsVisible = !filtered.Any() && _allSongs.Any();
    }

    // ── Song selection ───────────────────────────────────────

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

    // ── Continue / save ──────────────────────────────────────

    private async void OnNextClicked(object sender, EventArgs e)
    {
        await SpotifyPlayer.StopAsync();
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
                $"{_apiBaseUrl}/spotify/genres-from-artists?artists={Uri.EscapeDataString(string.Join(",", _selectedArtists))}");

            List<string> genres = new();
            if (genreResponse.IsSuccessStatusCode)
            {
                var json = await genreResponse.Content.ReadAsStringAsync();
                genres = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }

            var finalGenres = genres.Take(5).ToList();
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
                Application.Current!.MainPage = new NavigationPage(new MainPage());
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

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await SpotifyPlayer.StopAsync();
        StopCurrentSong();
    }
}

// ── ViewModel ────────────────────────────────────────────────
public class SongViewModel : BindableObject
{
    private string _title = "";
    private string _artist = "";
    private string? _spotifyUri;
    private string? _spotifyUrl;
    private bool _isSelected;
    private bool _isPlaying;
    private Color _borderColor = Colors.Transparent;
    private string _playButtonText = "▶";
    private Color _playButtonColor = Color.FromArgb("#1db954");

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Artist { get => _artist; set { _artist = value; OnPropertyChanged(); } }
    public string? SpotifyUri { get => _spotifyUri; set { _spotifyUri = value; OnPropertyChanged(); } }
    public string? SpotifyUrl { get => _spotifyUrl; set { _spotifyUrl = value; OnPropertyChanged(); } }
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }
    public Color BorderColor { get => _borderColor; set { _borderColor = value; OnPropertyChanged(); } }
    public string PlayButtonText { get => _playButtonText; set { _playButtonText = value; OnPropertyChanged(); } }
    public Color PlayButtonColor { get => _playButtonColor; set { _playButtonColor = value; OnPropertyChanged(); } }
}

// ── DTOs ─────────────────────────────────────────────────────
public class SpotifySong
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? PreviewUrl { get; set; }
    public string? SpotifyUri { get; set; }
    public string? SpotifyUrl { get; set; }
    public string? DeezerPreviewUrl { get; set; }
}
