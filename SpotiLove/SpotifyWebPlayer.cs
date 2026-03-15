// ============================================================
// ADD THIS FILE to your SpotiLove MAUI project
// File: SpotiLove/SpotifyWebPlayer.cs
// ============================================================

using System.Diagnostics;
using System.Text.Json;

namespace SpotiLove;

public class SpotifyWebPlayer : ContentView
{
    // ── Events that SongSelectionPage listens to ─────────────
    public event Action? PlayerReady;
    public event Action<bool>? PlaybackStateChanged;   // true = playing
    public event Action<string>? PlaybackError;

    private readonly WebView _webView;
    private bool _isReady = false;
    private string? _pendingUri = null;     // queued track while player warms up
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";

    public bool IsReady => _isReady;

    public SpotifyWebPlayer()
    {
        _webView = new WebView
        {
            IsVisible = false,   // fully hidden — audio only
            HeightRequest = 1,
            WidthRequest = 1,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };

        // Intercept JS → C# messages
        _webView.Navigating += OnWebViewNavigating;

        Content = _webView;
        HeightRequest = 1;
        WidthRequest = 1;
        IsVisible = false;
    }

    // ── Public API ───────────────────────────────────────────

    /// Call this once, passing the logged-in user's ID.
    /// Fetches the Spotify token from your backend and loads the player page.
    public async Task InitializeAsync(Guid userId)
    {
        try
        {
            Debug.WriteLine($"[SpotifyWebPlayer] Initializing for user {userId}");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync($"{_apiBaseUrl}/player/token/{userId}");

            if (!response.IsSuccessStatusCode)
            {
                PlaybackError?.Invoke("No Spotify token — please log in with Spotify first.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TokenResponse>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (string.IsNullOrEmpty(result?.Token))
            {
                PlaybackError?.Invoke("Empty Spotify token returned from server.");
                return;
            }

            var playerUrl = $"{_apiBaseUrl}/player?token={Uri.EscapeDataString(result.Token)}";
            Debug.WriteLine($"[SpotifyWebPlayer] Loading player URL: {playerUrl}");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _webView.Source = new UrlWebViewSource { Url = playerUrl };
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyWebPlayer] Init error: {ex.Message}");
            PlaybackError?.Invoke($"Player init failed: {ex.Message}");
        }
    }

    /// Play a Spotify track URI (e.g. "spotify:track:abc123")
    public async Task PlayAsync(string spotifyUri)
    {
        if (!_isReady)
        {
            Debug.WriteLine("[SpotifyWebPlayer] Not ready yet — queuing track");
            _pendingUri = spotifyUri;
            return;
        }

        await RunJsAsync($"window.playTrack('{spotifyUri}')");
    }

    public async Task PauseAsync() => await RunJsAsync("window.pauseTrack()");
    public async Task ResumeAsync() => await RunJsAsync("window.resumeTrack()");
    public async Task StopAsync() => await RunJsAsync("window.stopTrack()");
    public async Task SetVolumeAsync(double volume) =>
        await RunJsAsync($"window.setVolume({volume.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)})");

    // ── Private helpers ──────────────────────────────────────

    private async Task RunJsAsync(string js)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _webView.EvaluateJavaScriptAsync(js);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyWebPlayer] JS error: {ex.Message}");
        }
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("spotilove-event://"))
            return;

        // Cancel the navigation — we just want the data
        e.Cancel = true;

        try
        {
            var uri = new Uri(e.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var eventType = uri.Host;   // e.g. "ready", "state_changed", "error"
            var dataJson = query["data"] ?? "{}";

            Debug.WriteLine($"[SpotifyWebPlayer] Event: {eventType} | Data: {dataJson}");

            using var doc = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;

            switch (eventType)
            {
                case "ready":
                    _isReady = true;
                    PlayerReady?.Invoke();

                    // Play queued track if any
                    if (_pendingUri != null)
                    {
                        var uri2 = _pendingUri;
                        _pendingUri = null;
                        _ = PlayAsync(uri2);
                    }
                    break;

                case "state_changed":
                    var paused = root.TryGetProperty("paused", out var p) && p.GetBoolean();
                    PlaybackStateChanged?.Invoke(!paused);
                    break;

                case "error":
                    var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";
                    Debug.WriteLine($"[SpotifyWebPlayer] Error ({type}): {msg}");
                    PlaybackError?.Invoke($"{type}: {msg}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyWebPlayer] Parse error: {ex.Message}");
        }
    }

    private class TokenResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
    }
}
