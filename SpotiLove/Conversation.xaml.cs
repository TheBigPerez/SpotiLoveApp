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
    private readonly string _apiBaseUrl = "https://spotilove.danielnaz.com";

    // Poll for new messages every 5 seconds
    private System.Timers.Timer? _pollTimer;

    public Conversation(ChatViewModel chat)
    {
        InitializeComponent();
        _chat = chat;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        // Set header info
        UserNameLabel.Text = chat.Name;
        ProfileImage.Source = string.IsNullOrEmpty(chat.ProfileImage) ? "default_user.png" : chat.ProfileImage;
        StatusLabel.Text = chat.IsOnline ? "Online" : "Active recently";
        StatusLabel.TextColor = chat.IsOnline
            ? Color.FromArgb("#1db954")
            : Color.FromArgb("#888888");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadMessages();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopPolling();
    }

    // =========================================================
    // Polling for new messages
    // =========================================================

    private void StartPolling()
    {
        _pollTimer = new System.Timers.Timer(5000); // every 5 seconds
        _pollTimer.Elapsed += async (s, e) =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await LoadMessages(scrollToBottom: false);
            });
        };
        _pollTimer.AutoReset = true;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    // =========================================================
    // Load Messages from API
    // =========================================================

    private async Task LoadMessages(bool scrollToBottom = true)
    {
        try
        {
            if (UserData.Current == null) return;

            var response = await _httpClient.GetAsync(
                $"{_apiBaseUrl}/chats/{UserData.Current.Id}/messages/{_chat.UserId}"
            );

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Failed to load messages: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MessagesApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Messages == null) return;

            // Rebuild the messages container
            MessagesContainer.Clear();

            if (!result.Messages.Any())
            {
                AddEmptyState();
                return;
            }

            // Group messages by date
            var grouped = result.Messages
                .OrderBy(m => m.SentAt)
                .GroupBy(m => m.SentAt.ToLocalTime().Date);

            foreach (var group in grouped)
            {
                // Date separator
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

                    if (isOutgoing)
                        AddOutgoingMessage(msg.Content, time, msg.IsRead);
                    else
                        AddIncomingMessage(msg.Content, time, _chat.Name);
                }
            }

            if (scrollToBottom)
                await ScrollToBottom();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading messages: {ex.Message}");
        }
    }

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
                    HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center
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
        contentStack.Add(new Label { Text = message, FontSize = 14, TextColor = Colors.White, LineBreakMode = LineBreakMode.WordWrap });
        contentStack.Add(new Label { Text = time, FontSize = 10, TextColor = Color.FromArgb("#888888"), HorizontalOptions = LayoutOptions.End });
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

        var bottomStack = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 5 };
        bottomStack.Add(new Label { Text = time, FontSize = 10, TextColor = Color.FromArgb("#f0f0f0") });
        bottomStack.Add(new Label { Text = isRead ? "✓✓" : "✓", FontSize = 12, TextColor = Color.FromArgb("#f0f0f0") });

        var contentStack = new VerticalStackLayout { Spacing = 4 };
        contentStack.Add(new Label { Text = message, FontSize = 14, TextColor = Colors.White, LineBreakMode = LineBreakMode.WordWrap });
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

    private async Task ScrollToBottom()
    {
        await Task.Delay(100);
        await MessagesScrollView.ScrollToAsync(0, MessagesContainer.Height, false);
    }

    // =========================================================
    // Send Message
    // =========================================================

    private async void OnSendMessageClicked(object sender, EventArgs e)
    {
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message)) return;
        if (UserData.Current == null) return;

        // Clear input immediately for UX
        var sentText = message;
        MessageEntry.Text = string.Empty;

        // Add to UI immediately (optimistic)
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
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync($"{_apiBaseUrl}/chats/send", content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Send message failed: {err}");
                await DisplayAlert("Error", "Message could not be delivered. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending message: {ex.Message}");
            await DisplayAlert("Error", "Failed to send message.", "OK");
        }
    }

    // =========================================================
    // Other UI Events
    // =========================================================

    private async void OnShareMusicClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Share Music", "Music sharing coming soon! 🎵", "OK");
    }

    private async void OnAttachClicked(object sender, EventArgs e)
    {
        try
        {
            var action = await DisplayActionSheet("Share", "Cancel", null, "📷 Photo", "🎵 Music");
            if (action == "🎵 Music")
                await DisplayAlert("Share Music", "Music sharing coming soon!", "OK");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Attachment error: {ex.Message}");
        }
    }

    private async void OnMoreOptionsClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheet("Options", "Cancel", null, "View Profile", "Block User");
        if (action == "Block User")
        {
            bool confirm = await DisplayAlert("Block User", $"Block {_chat.Name}? You won't see each other anymore.", "Block", "Cancel");
            if (confirm)
                await Navigation.PopAsync();
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
