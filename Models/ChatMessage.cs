using System.ComponentModel.DataAnnotations;

namespace ChatApp.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;

        [Required]
        public string SenderName { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string? RoomId { get; set; }
    }
}
