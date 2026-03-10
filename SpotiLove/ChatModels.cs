using System;
using System.Collections.Generic;

namespace SpotiLove;

// ===== CHAT VIEW MODEL =====
public class ChatViewModel : BindableObject
{
    private Guid _userId;
    private string _name = "";
    private string _profileImage = "";
    private string _lastMessage = "";
    private string _timeStamp = "";
    private bool _hasUnread;
    private int _unreadCount;
    private bool _isOnline;
    public Guid UserId
    {
        get => _userId;
        set { _userId = value; OnPropertyChanged(); }
    }
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string ProfileImage
    {
        get => _profileImage;
        set { _profileImage = value; OnPropertyChanged(); }
    }

    public string LastMessage
    {
        get => _lastMessage;
        set { _lastMessage = value; OnPropertyChanged(); }
    }

    public string TimeStamp
    {
        get => _timeStamp;
        set { _timeStamp = value; OnPropertyChanged(); }
    }

    public bool HasUnread
    {
        get => _hasUnread;
        set { _hasUnread = value; OnPropertyChanged(); }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set { _unreadCount = value; OnPropertyChanged(); }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set { _isOnline = value; OnPropertyChanged(); }
    }
}

// ===== MESSAGE VIEW MODEL =====
public class MessageViewModel : BindableObject
{
    private Guid _id;
    private Guid _senderId;
    private Guid _receiverId;
    private string _content = "";
    private DateTime _sentAt;
    private bool _isRead;
    private MessageType _type;
    private string? _musicData;

    public Guid Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public Guid SenderId
    {
        get => _senderId;
        set { _senderId = value; OnPropertyChanged(); }
    }

    public Guid ReceiverId
    {
        get => _receiverId;
        set { _receiverId = value; OnPropertyChanged(); }
    }

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    public DateTime SentAt
    {
        get => _sentAt;
        set { _sentAt = value; OnPropertyChanged(); }
    }

    public bool IsRead
    {
        get => _isRead;
        set { _isRead = value; OnPropertyChanged(); }
    }

    public MessageType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(); }
    }

    public string? MusicData
    {
        get => _musicData;
        set { _musicData = value; OnPropertyChanged(); }
    }

    public bool IsOutgoing => SenderId == UserData.Current?.Id;

    public string FormattedTime => SentAt.ToString("h:mm tt");
}

public enum MessageType
{
    Text,
    Music,
    Image,
    Location
}

// ===== API RESPONSE MODELS =====
public class MatchesResponse
{
    public List<UserDto>? Matches { get; set; }
    public int Count { get; set; }
    public string? Message { get; set; }
}