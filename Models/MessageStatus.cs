namespace ChatApp.Models
{
    public class MessageStatus
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        public string MessageType { get; set; } = "group";

        public virtual ApplicationUser? User { get; set; }
    }
}
