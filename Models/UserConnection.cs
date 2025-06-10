namespace ChatApp.Models
{
    public class UserConnection
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        public virtual ApplicationUser? User { get; set; }
    }
}
