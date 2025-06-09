using Microsoft.AspNetCore.Identity;

namespace ChatApp.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }
}
