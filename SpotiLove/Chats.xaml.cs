using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace SpotiLove;

public partial class Chats : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";
    private ObservableCollection<ChatViewModel> _allChats = new();
    private ObservableCollection<ChatViewModel> _filteredChats = new();

    public Chats()
    {
        InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        ChatsCollection.ItemsSource = _filteredChats;

        Resources.Add("BoolToColorConverter", new BoolToColorConverter());
        Resources.Add("UnreadMessageColorConverter", new UnreadMessageColorConverter());
        Resources.Add("UnreadMessageFontConverter", new UnreadMessageFontConverter());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadChats();
    }

    private async Task LoadChats()
    {
        try
        {
            if (UserData.Current == null || UserData.Current.Id == Guid.Empty)
            {
                await DisplayAlert("Error", "Please log in first", "OK");
                return;
            }

            Debug.WriteLine($"Loading chats/matches for user: {UserData.Current.Id}");

            // ── 1. Load existing conversations (users we've messaged) ──────────
            await LoadConversations();

            // ── 2. Also load matches who haven't been messaged yet ─────────────
            await LoadMatchesWithoutConversation();

            UpdateFilteredChats(ChatSearchBar.Text);
            EmptyStateView.IsVisible = !_allChats.Any();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading chats: {ex.Message}");
            await DisplayAlert("Error", "Failed to load chats. Please try again.", "OK");
        }
    }

    private async Task LoadConversations()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/chats/{UserData.Current!.Id}/conversations"
            );

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Conversations endpoint returned: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ConversationsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _allChats.Clear();

            if (result?.Conversations != null)
            {
                foreach (var conv in result.Conversations)
                {
                    _allChats.Add(new ChatViewModel
                    {
                        UserId = conv.UserId,
                        Name = conv.Name ?? "Unknown",
                        ProfileImage = conv.ProfileImage ?? "default_user.png",
                        LastMessage = conv.LastMessage ?? "Say hi 👋",
                        TimeStamp = FormatTimestamp(conv.LastMessageTime),
                        HasUnread = conv.UnreadCount > 0,
                        UnreadCount = conv.UnreadCount,
                        IsOnline = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadConversations error: {ex.Message}");
        }
    }

    private async Task LoadMatchesWithoutConversation()
    {
        try
        {
            // The matches endpoint returns { Matches: [...], Count: N, Message: "" }
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/matches/{UserData.Current!.Id}");

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Matches endpoint returned: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MatchesApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Matches == null) return;

            // Only add matches that don't already have a conversation entry
            var existingUserIds = _allChats.Select(c => c.UserId).ToHashSet();

            foreach (var match in result.Matches)
            {
                if (!existingUserIds.Contains(match.Id))
                {
                    _allChats.Add(new ChatViewModel
                    {
                        UserId = match.Id,
                        Name = match.Name ?? "Unknown",
                        ProfileImage = match.Images?.FirstOrDefault() ?? "default_user.png",
                        LastMessage = "You matched! Say hi 👋",
                        TimeStamp = "New",
                        HasUnread = false,
                        UnreadCount = 0,
                        IsOnline = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadMatchesWithoutConversation error: {ex.Message}");
        }
    }

    private string FormatTimestamp(DateTime? dt)
    {
        if (dt == null) return "";
        var local = dt.Value.ToLocalTime();
        if (local.Date == DateTime.Today) return local.ToString("h:mm tt");
        if (local.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
        return local.ToString("MMM d");
    }

    private void UpdateFilteredChats(string? searchText = null)
    {
        _filteredChats.Clear();

        var source = string.IsNullOrWhiteSpace(searchText)
            ? _allChats
            : _allChats.Where(c =>
                c.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                c.LastMessage.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var chat in source)
            _filteredChats.Add(chat);

        EmptyStateView.IsVisible = !_filteredChats.Any();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFilteredChats(e.NewTextValue);
    }

    private async void OnChatTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is ChatViewModel chat)
        {
            chat.HasUnread = false;
            chat.UnreadCount = 0;
            await Navigation.PushAsync(new Conversation(chat));
        }
    }

    private async void OnNewChatClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Matches", "Only your mutual matches appear here. Keep swiping to find more!", "OK");
    }

    // =========================================================
    // API Response Models
    // =========================================================

    // Matches endpoint: { Matches: [...], Count: N, Message: "" }
    private class MatchesApiResponse
    {
        public List<UserDto>? Matches { get; set; }
        public int Count { get; set; }
        public string? Message { get; set; }
    }

    // Conversations endpoint: { success: true, conversations: [...] }
    private class ConversationsResponse
    {
        public bool Success { get; set; }
        public List<ConversationDto>? Conversations { get; set; }
    }

    private class ConversationDto
    {
        public Guid UserId { get; set; }
        public string? Name { get; set; }
        public string? ProfileImage { get; set; }
        public string? LastMessage { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public int UnreadCount { get; set; }
        public bool IsOnline { get; set; }
    }
}
