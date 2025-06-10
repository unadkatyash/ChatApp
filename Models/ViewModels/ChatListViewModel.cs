namespace ChatApp.Models.ViewModels
{
    public class ChatListViewModel
    {
        public List<UserChatInfo> Users { get; set; } = new();
        public List<ChatMessage> GroupMessages { get; set; } = new();
    }

    public class UserChatInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public string? LastMessage { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public int UnreadCount { get; set; }
    }
}
