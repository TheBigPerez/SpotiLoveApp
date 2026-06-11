using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SpotiLove;

public partial class Conversation : ContentPage
{
    private readonly ChatViewModel _chat;
    private readonly HttpClient _httpClient;
    private readonly PlaylistService _playlistService;
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";

    private System.Timers.Timer? _pollTimer;
    private bool _isLoadingMessages = false;

    private List<Guid> _lastMsgIds = new();
    private bool _isSending = false;

    public Conversation(ChatViewModel chat)
    {
        InitializeComponent();
        _chat = chat;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _playlistService = new PlaylistService();

        UserNameLabel.Text = chat.Name;
        ProfileImage.Source = ImageHelper.Resolve(chat.ProfileImage);
        StatusLabel.Text = chat.IsOnline ? "Online" : "Active recently";
        StatusLabel.TextColor = chat.IsOnline
            ? Color.FromArgb("#1db954")
            : Color.FromArgb("#888888");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Debug.WriteLine("[Conversation] OnAppearing — resetting state");
        await LoadMessages();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Debug.WriteLine("[Conversation] OnDisappearing — stopping poll timer");
        StopPolling();
    }

    // =========================================================
    // Polling
    // =========================================================

    private void StartPolling()
    {
        if (_isSending) return;
        _pollTimer = new System.Timers.Timer(5000);
        _pollTimer.Elapsed += async (s, e) =>
        {
            if (_isSending) return;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await LoadMessages(scrollToBottom: false);
            });
        };
        _pollTimer.AutoReset = true;
        _pollTimer.Start();
        Debug.WriteLine("[Conversation] Poll timer started");
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null; // FIX 3: must null out so guard works on next OnAppearing
        Debug.WriteLine("[Conversation] Poll timer stopped");
    }

    // =========================================================
    // Load Messages
    // =========================================================

    private async Task LoadMessages(
        bool scrollToBottom = true,
        bool forceRebuild = false)
    {
        if (_isLoadingMessages) return;
        _isLoadingMessages = true;

        try
        {
            if (UserData.Current == null) return;

            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/chats/{UserData.Current.Id}/messages/{_chat.UserId}"
            );

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[Conversation] Failed to load messages: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MessagesApiResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Messages == null)
            {
                Debug.WriteLine("[Conversation] Messages response was null or malformed");
                return;
            }

            // FIX 1: compare IDs — skip rebuild if nothing changed
            var newIds = result.Messages
                .OrderBy(m => m.SentAt)
                .Select(m => m.Id)
                .ToList();

            Debug.WriteLine($"[Conversation] LoadMessages — got {result.Messages.Count} messages from server");
            Debug.WriteLine($"[Conversation] LoadMessages — _lastMsgIds has {_lastMsgIds.Count}");

            bool hasKnownState = _lastMsgIds.Count > 0;
            bool changed = !newIds.SequenceEqual(_lastMsgIds);

            Debug.WriteLine($"[Conversation] hasKnownState={hasKnownState}, changed={changed}");
            if (!forceRebuild)
            {
                if (hasKnownState && !changed) return;
                if (hasKnownState && newIds.Count < _lastMsgIds.Count) return;
                if (hasKnownState && !newIds.Any()) return;
            }

            _lastMsgIds = newIds;

            Debug.WriteLine("[Conversation] MessagesContainer cleared, rebuilding...");
            MessagesContainer.Clear();

            if (!result.Messages.Any())
            {
                AddEmptyState();
                return;
            }

            var grouped = result.Messages
                .OrderBy(m => m.SentAt)
                .GroupBy(m => m.SentAt.ToLocalTime().Date);

            foreach (var group in grouped)
            {
                string dateLabel = group.Key.Date == DateTime.Today
                    ? "Today"
                    : group.Key.Date == DateTime.Today.AddDays(-1)
                        ? "Yesterday"
                        : group.Key.ToString("MMMM d, yyyy");

                AddDateSeparator(dateLabel);

                foreach (var msg in group)
                {
                    bool isOutgoing = msg.FromUserId == UserData.Current.Id;
                    string time = msg.SentAt.ToLocalTime().ToString("h:mm tt");

                    if (msg.Content.StartsWith("🎵") && msg.Content.Contains("spotify.com"))
                        AddPlaylistMessage(msg.Content, time, isOutgoing);
                    else if (isOutgoing)
                        AddOutgoingMessage(msg.Content, time, msg.IsRead);
                    else
                        AddIncomingMessage(msg.Content, time, _chat.Name);
                }
            }

            Debug.WriteLine($"[Conversation] Rebuild complete — {MessagesContainer.Count} views in container");

            if (scrollToBottom)
                await ScrollToBottom();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Conversation] Error loading messages: {ex.Message}");
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    // =========================================================
    // Send Message
    // =========================================================

    private async void OnSendMessageClicked(object sender, EventArgs e)
    {
        _isSending = true;
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message)) return;
        if (UserData.Current == null) return;

        var sentText = message;
        MessageEntry.Text = string.Empty;

        var time = DateTime.Now.ToString("h:mm tt");
        AddOutgoingMessage(sentText, time, false);
        await ScrollToBottom();

        try
        {
            var payload = new
            {
                fromUserId = UserData.Current.Id,
                toUserId = _chat.UserId,
                content = sentText
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/chats/send", content);
            Debug.WriteLine($"Send status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                // DO NOT wipe _lastMsgIds. let change-detect swap bubble -> real.
                await Task.Delay(800);
                await LoadMessages(scrollToBottom: true);
            }
            else
            {
                // send fail -> force real state, kill the fake bubble
                await LoadMessages(scrollToBottom: false, forceRebuild: true);
                await DisplayAlert("Error", "Message could not be delivered. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Conversation] Error sending message: {ex.Message}");
            await LoadMessages(scrollToBottom: false, forceRebuild: true);
            await DisplayAlert("Error", "Failed to send message.", "OK");
        }
        finally
        {
            _isSending = false;
        }
    }

    // =========================================================
    // System message (used by playlist)
    // =========================================================

    private async Task SendSystemMessage(string text)
    {
        if (UserData.Current == null) return;
        try
        {
            var payload = new
            {
                fromUserId = UserData.Current.Id,
                toUserId = _chat.UserId,
                content = text
            };
            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"{_apiBaseUrl}/chats/send", content);
            await Task.Delay(800);
            await LoadMessages();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Conversation] SendSystemMessage error: {ex.Message}");
        }
    }

    // =========================================================
    // Shared Playlist
    // =========================================================

    private async void OnCreatePlaylistClicked(object sender, EventArgs e)
    {
        if (UserData.Current == null) return;

        CreatePlaylistButton.IsEnabled = false;
        CreatePlaylistButton.Text = "...";

        try
        {
            bool confirmed = await DisplayAlert(
                "Create Shared Playlist 🎵",
                $"This will mix your favourite songs with {_chat.Name}'s into a Spotify playlist.\n\nThis can take up to 30 seconds.",
                "Let's go!", "Cancel");

            if (!confirmed) { ResetPlaylistButton(); return; }

            var bannerLabel = ((Grid)PlaylistBanner.Content).Children
                .OfType<VerticalStackLayout>().FirstOrDefault()
                ?.Children.OfType<Label>().LastOrDefault();

            if (bannerLabel != null)
                bannerLabel.Text = "Searching Spotify for your songs…";

            var result = await _playlistService.CreateMatchPlaylistAsync(
                UserData.Current.Id, _chat.UserId);

            if (result.Success && result.PlaylistUrl != null)
            {
                await ShowPlaylistSuccessBanner(result.PlaylistUrl, result.Message);
                await SendSystemMessage(
                    $"🎵 We created a shared playlist! Open it in Spotify: {result.PlaylistUrl}");
            }
            else
            {
                ResetPlaylistButton();
                if (bannerLabel != null)
                    bannerLabel.Text = "Mix your favourite songs into one Spotify playlist";
                await DisplayAlert("Couldn't Create Playlist", result.Message, "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Conversation] OnCreatePlaylistClicked error: {ex.Message}");
            ResetPlaylistButton();
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task ShowPlaylistSuccessBanner(string playlistUrl, string message)
    {
        PlaylistBanner.BackgroundColor = Color.FromArgb("#1a2e1a");

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var textStack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2 };
        textStack.Add(new Label
        {
            Text = "🎵 Your Playlist is Ready!",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        });
        textStack.Add(new Label
        {
            Text = message,
            FontSize = 12,
            TextColor = Color.FromArgb("#1db954")
        });
        Grid.SetColumn(textStack, 0);

        var openBtn = new Button
        {
            Text = "Open ↗",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#121212"),
            BackgroundColor = Color.FromArgb("#1db954"),
            CornerRadius = 15,
            Padding = new Thickness(14, 8),
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        openBtn.Clicked += async (_, _) =>
        {
            try { await Launcher.OpenAsync(new Uri(playlistUrl)); }
            catch { await DisplayAlert("Open Spotify", $"Playlist URL:\n{playlistUrl}", "OK"); }
        };
        Grid.SetColumn(openBtn, 1);

        grid.Children.Add(textStack);
        grid.Children.Add(openBtn);

        PlaylistBanner.Content = new Border
        {
            Content = grid,
            Padding = new Thickness(15, 10),
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0
        };

        await Task.CompletedTask;
    }

    private void ResetPlaylistButton()
    {
        CreatePlaylistButton.IsEnabled = true;
        CreatePlaylistButton.Text = "Create";
    }

    // =========================================================
    // Message bubble builders
    // =========================================================

    private void AddEmptyState()
    {
        MessagesContainer.Add(new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 10,
            Margin = new Thickness(0, 40),
            Children =
            {
                new Label { Text = "💬", FontSize = 50, HorizontalOptions = LayoutOptions.Center },
                new Label
                {
                    Text = $"You matched with {_chat.Name}!",
                    FontSize = 18, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Say hi and start the conversation 👋",
                    FontSize = 14, TextColor = Color.FromArgb("#b3b3b3"),
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        });
    }

    private void AddDateSeparator(string dateText)
    {
        MessagesContainer.Add(new Label
        {
            Text = dateText,
            FontSize = 12,
            TextColor = Color.FromArgb("#888888"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10)
        });
    }

    private void AddIncomingMessage(string message, string time, string senderName)
    {
        var messageBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#212121"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15, 15, 15, 0) },
            Stroke = Colors.Transparent,
            Padding = new Thickness(12, 8),
            MaximumWidthRequest = 270
        };
        var contentStack = new VerticalStackLayout { Spacing = 4 };
        contentStack.Add(new Label
        {
            Text = message,
            FontSize = 14,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap
        });
        contentStack.Add(new Label
        {
            Text = time,
            FontSize = 10,
            TextColor = Color.FromArgb("#888888"),
            HorizontalOptions = LayoutOptions.End
        });
        messageBorder.Content = contentStack;

        var row = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 3)
        };
        row.Add(messageBorder);
        MessagesContainer.Add(row);
    }

    private void AddOutgoingMessage(string message, string time, bool isRead = true)
    {
        var messageBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#1db954"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15, 15, 0, 15) },
            Stroke = Colors.Transparent,
            Padding = new Thickness(12, 8),
            MaximumWidthRequest = 270
        };
        var bottomStack = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            Spacing = 5
        };
        bottomStack.Add(new Label
        {
            Text = time,
            FontSize = 10,
            TextColor = Color.FromArgb("#f0f0f0")
        });
        bottomStack.Add(new Label
        {
            Text = isRead ? "✓✓" : "✓",
            FontSize = 12,
            TextColor = Color.FromArgb("#f0f0f0")
        });

        var contentStack = new VerticalStackLayout { Spacing = 4 };
        contentStack.Add(new Label
        {
            Text = message,
            FontSize = 14,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap
        });
        contentStack.Add(bottomStack);
        messageBorder.Content = contentStack;

        var row = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 3)
        };
        row.Add(messageBorder);
        MessagesContainer.Add(row);
    }

    private void AddPlaylistMessage(string message, string time, bool isOutgoing)
    {
        var parts = message.Split(' ');
        var url = parts.LastOrDefault(p => p.StartsWith("http")) ?? "";
        var preview = message.Contains("http")
            ? message[..message.LastIndexOf("http")].Trim() : message;

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1a2e1a"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Stroke = Color.FromArgb("#1db954"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 10),
            MaximumWidthRequest = 290
        };

        var inner = new VerticalStackLayout { Spacing = 8 };
        inner.Add(new Label
        {
            Text = "🎵 Shared Playlist",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1db954")
        });
        inner.Add(new Label
        {
            Text = preview,
            FontSize = 13,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap
        });

        if (!string.IsNullOrEmpty(url))
        {
            var openBtn = new Button
            {
                Text = "Open in Spotify ↗",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#121212"),
                BackgroundColor = Color.FromArgb("#1db954"),
                CornerRadius = 12,
                HeightRequest = 36,
                Padding = new Thickness(12, 6)
            };
            var capturedUrl = url;
            openBtn.Clicked += async (_, _) =>
            {
                try { await Launcher.OpenAsync(new Uri(capturedUrl)); }
                catch { await DisplayAlert("Open Spotify", capturedUrl, "OK"); }
            };
            inner.Add(openBtn);
        }

        inner.Add(new Label
        {
            Text = time,
            FontSize = 10,
            TextColor = Color.FromArgb("#888888"),
            HorizontalOptions = LayoutOptions.End
        });
        card.Content = inner;

        var row = new HorizontalStackLayout
        {
            HorizontalOptions = isOutgoing ? LayoutOptions.End : LayoutOptions.Start,
            Margin = new Thickness(0, 3)
        };
        row.Add(card);
        MessagesContainer.Add(row);
    }

    private async Task ScrollToBottom()
    {
        await Task.Delay(100);
        await MessagesScrollView.ScrollToAsync(0, MessagesContainer.Height, false);
    }

    // =========================================================
    // Other UI Events
    // =========================================================

    private async void OnShareMusicClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Share Music",
            "Use the 'Create Playlist' banner above to build a shared playlist! 🎵", "Got it");
    }

    private async void OnAttachClicked(object sender, EventArgs e)
    {
        try
        {
            var action = await DisplayActionSheet("Share", "Cancel", null,
                "📷 Photo", "🎵 Create Playlist");
            if (action == "🎵 Create Playlist")
                OnCreatePlaylistClicked(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Attachment error: {ex.Message}");
        }
    }

    private async void OnMoreOptionsClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheet("Options", "Cancel", null,
            "View Profile", "🎵 Create Playlist", "Block User");

        if (action == "🎵 Create Playlist")
            OnCreatePlaylistClicked(this, EventArgs.Empty);
        else if (action == "Block User")
        {
            bool confirm = await DisplayAlert("Block User",
                $"Block {_chat.Name}? You won't see each other anymore.", "Block", "Cancel");
            if (confirm) await Navigation.PopAsync();
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    // =========================================================
    // API Response Models
    // =========================================================

    private class MessagesApiResponse
    {
        public bool Success { get; set; }
        public List<MessageDto>? Messages { get; set; }
        public int Count { get; set; }
    }

    private class MessageDto
    {
        public Guid Id { get; set; }
        public Guid FromUserId { get; set; }
        public Guid ToUserId { get; set; }
        public string Content { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}