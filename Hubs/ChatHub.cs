using ChatApp.Data;
using ChatApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ChatApp.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task SendMessage(string message, string roomId = "general")
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = Context.User?.FindFirst(ClaimTypes.Name)?.Value;
            var firstName = Context.User?.FindFirst("FirstName")?.Value;
            var lastName = Context.User?.FindFirst("LastName")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userName))
                return;

            var displayName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(displayName))
                displayName = userName;

            var chatMessage = new ChatMessage
            {
                SenderId = userId,
                SenderName = displayName,
                Message = message,
                RoomId = roomId,
                Timestamp = DateTime.UtcNow
            };

            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();

            await Clients.Group(roomId).SendAsync("ReceiveMessage", displayName, message, chatMessage.Timestamp.ToString("HH:mm"));
        }

        public async Task JoinRoom(string roomId = "general")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            var user = Context.User?.FindFirst(ClaimTypes.Name)?.Value;
            var userName = Context.User?.FindFirst("FirstName")?.Value + " " + Context.User?.FindFirst("LastName")?.Value;
            await Clients.Group(roomId).SendAsync("UserJoined", userName, user);
        }

        public async Task LeaveRoom(string roomId = "general")
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

            var user = Context.User?.FindFirst(ClaimTypes.Name)?.Value;
            var userName = Context.User?.FindFirst("FirstName")?.Value + " " + Context.User?.FindFirst("LastName")?.Value;
            await Clients.Group(roomId).SendAsync("UserLeft", userName, user);
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.IsOnline = true;
                    user.LastSeen = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            await JoinRoom("general");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.IsOnline = false;
                    user.LastSeen = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
