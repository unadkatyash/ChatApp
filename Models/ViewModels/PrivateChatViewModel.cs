namespace ChatApp.Models.ViewModels
{
    public class PrivateChatViewModel
    {
        public string ReceiverId { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public List<PrivateMessage> Messages { get; set; } = new();
        public List<ApplicationUser> OnlineUsers { get; set; } = new();
    }
}
