// ================================================================
// NEW FILE: SpotiLove/PlaylistService.cs
// Place in: C:\Users\User\source\repos\SpotiLoveApp\SpotiLove\
// ================================================================

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SpotiLove;

public class PlaylistService
{
    private readonly HttpClient _httpClient;
    private const string ApiBaseUrl = "https://spotilove.danielnaz.com";

    public PlaylistService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// Creates a shared Spotify playlist for two matched users.
    /// Returns (success, playlistUrl, message).
    public async Task<PlaylistResult> CreateMatchPlaylistAsync(Guid userId, Guid matchedUserId)
    {
        try
        {
            Debug.WriteLine($"[PlaylistService] Creating playlist for {userId} & {matchedUserId}");

            var response = await _httpClient.PostAsync(
                $"{ApiBaseUrl}/matches/{userId}/create-playlist/{matchedUserId}",
                new StringContent("", Encoding.UTF8, "application/json"));

            var body = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[PlaylistService] Response {(int)response.StatusCode}: {body}");

            if (!response.IsSuccessStatusCode)
            {
                var err = TryExtractMessage(body);
                return new PlaylistResult(false, null, null, err ?? "Failed to create playlist");
            }

            var result = JsonSerializer.Deserialize<PlaylistApiResponse>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Success == true)
            {
                return new PlaylistResult(
                    true,
                    result.PlaylistId,
                    result.PlaylistUrl,
                    result.Message ?? "Playlist created!");
            }

            return new PlaylistResult(false, null, null, result?.Message ?? "Unknown error");
        }
        catch (TaskCanceledException)
        {
            return new PlaylistResult(false, null, null,
                "Request timed out — playlist creation can take up to 60 seconds.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlaylistService] Error: {ex.Message}");
            return new PlaylistResult(false, null, null, ex.Message);
        }
    }

    private static string? TryExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString();
            if (doc.RootElement.TryGetProperty("detail",  out var d)) return d.GetString();
            if (doc.RootElement.TryGetProperty("title",   out var t)) return t.GetString();
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }

    // ── Response DTOs ─────────────────────────────────────────

    private class PlaylistApiResponse
    {
        public bool   Success     { get; set; }
        public string? PlaylistId  { get; set; }
        public string? PlaylistUrl { get; set; }
        public int    TrackCount  { get; set; }
        public string? Message     { get; set; }
    }
}

public record PlaylistResult(
    bool    Success,
    string? PlaylistId,
    string? PlaylistUrl,
    string  Message
);
