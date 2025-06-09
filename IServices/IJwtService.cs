using ChatApp.Models;

namespace ChatApp.IServices
{
    public interface IJwtService
    {
        string GenerateToken(ApplicationUser user);
        bool ValidateToken(string token);
    }
}
