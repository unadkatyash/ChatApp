using ChatApp.Data;
using ChatApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace ChatApp.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private static ConcurrentDictionary<string, string> _connectionRooms = new ConcurrentDictionary<string, string>();

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
                Timestamp = DateTime.Now
            };

            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();

            var usersInRoom = await _context.UserConnections
                .Where(uc => uc.IsActive)
                .Include(uc => uc.User)
                .Select(uc => uc.UserId)
                .Distinct()
                .Where(uid => uid != userId)
                .ToListAsync();

            var receiverConnections = await _context.UserConnections
                .Where(uc => uc.IsActive)
                .Include(uc => uc.User)
                .Distinct()
                .Where(uid => uid.UserId != userId)
                .Select(uc => uc.ConnectionId)
                .ToListAsync();

            foreach (var uid in usersInRoom)
            {
                _context.MessageStatuses.Add(new MessageStatus
                {
                    MessageId = chatMessage.Id,
                    UserId = uid,
                    MessageType = "group",
                    IsRead = false
                });
            }
            await _context.SaveChangesAsync();

            await Clients.Group(roomId).SendAsync("ReceiveMessage", displayName, message,
                chatMessage.Timestamp.ToString("HH:mm"), chatMessage.Id, userId);

            await Clients.Clients(receiverConnections).SendAsync("ReceiveMessageNotification", displayName, message,
                chatMessage.Timestamp.ToString("HH:mm"), chatMessage.Id, userId);
        }

        public async Task SendPrivateMessage(string receiverId, string message)
        {
            var senderId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var senderName = Context.User?.FindFirst(ClaimTypes.Name)?.Value;
            var firstName = Context.User?.FindFirst("FirstName")?.Value;
            var lastName = Context.User?.FindFirst("LastName")?.Value;

            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId))
                return;

            var displayName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(displayName))
                displayName = senderName;

            var privateMessage = new PrivateMessage
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Message = message,
                Timestamp = DateTime.Now
            };

            _context.PrivateMessages.Add(privateMessage);
            await _context.SaveChangesAsync();

            var receiverConnections = await _context.UserConnections
                .Where(uc => uc.UserId == receiverId && uc.IsActive)
                .Select(uc => uc.ConnectionId)
                .ToListAsync();

            if (receiverConnections.Any())
            {
                await Clients.Clients(receiverConnections).SendAsync("ReceivePrivateMessage",
                    senderId, displayName, message, privateMessage.Timestamp.ToString("HH:mm"), privateMessage.Id);
            }
            else
            {
                await StoreNotificationForOfflineUser(receiverId, displayName, message);
            }

            await Clients.Caller.SendAsync("PrivateMessageSent", receiverId, message,
                privateMessage.Timestamp.ToString("HH:mm"), privateMessage.Id);
        }

        public async Task MarkMessageAsRead(int messageId, string messageType = "group")
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            if (messageType == "private")
            {
                var privateMessage = await _context.PrivateMessages
                    .FirstOrDefaultAsync(pm => pm.Id == messageId && pm.ReceiverId == userId);

                if (privateMessage != null && !privateMessage.IsRead)
                {
                    privateMessage.IsRead = true;
                    privateMessage.ReadAt = DateTime.Now;
                    await _context.SaveChangesAsync();

                    var senderConnections = await _context.UserConnections
                        .Where(uc => uc.UserId == privateMessage.SenderId && uc.IsActive)
                        .Select(uc => uc.ConnectionId)
                        .ToListAsync();

                    if (senderConnections.Any())
                    {
                        await Clients.Clients(senderConnections).SendAsync("MessageRead", messageId, "private");
                    }
                }
            }
            else
            {
                var messageStatus = await _context.MessageStatuses
                    .FirstOrDefaultAsync(ms => ms.MessageId == messageId && ms.UserId == userId && ms.MessageType == messageType);

                if (messageStatus != null && !messageStatus.IsRead)
                {
                    messageStatus.IsRead = true;
                    messageStatus.ReadAt = DateTime.Now;
                    await _context.SaveChangesAsync();

                    var chatMessage = await _context.ChatMessages.FindAsync(messageId);
                    if (chatMessage != null)
                    {
                        var senderConnections = await _context.UserConnections
                            .Where(uc => uc.UserId == chatMessage.SenderId && uc.IsActive)
                            .Select(uc => uc.ConnectionId)
                            .ToListAsync();

                        if (senderConnections.Any())
                        {
                            await Clients.Clients(senderConnections).SendAsync("MessageRead", messageId, "group");
                        }
                    }
                }
            }
        }

        public async Task JoinRoom(string roomId = "general")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            _connectionRooms.AddOrUpdate(Context.ConnectionId, roomId, (key, oldValue) => roomId);

            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = Context.User?.FindFirst("FirstName")?.Value + " " + Context.User?.FindFirst("LastName")?.Value;

            await Clients.Group(roomId).SendAsync("UserJoined", userName, userId);
        }

        public async Task LeaveRoom(string roomId = "general")
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            _connectionRooms.TryRemove(Context.ConnectionId, out _);

            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = Context.User?.FindFirst("FirstName")?.Value + " " + Context.User?.FindFirst("LastName")?.Value;

            await Clients.Group(roomId).SendAsync("UserLeft", userName, userId);
        }

        public async Task GetUnreadMessageCount()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            var groupUnreadCount = await _context.MessageStatuses
                .CountAsync(ms => ms.UserId == userId && ms.MessageType == "group" && !ms.IsRead);

            var privateUnreadCount = await _context.PrivateMessages
                .CountAsync(pm => pm.ReceiverId == userId && !pm.IsRead);

            await Clients.Caller.SendAsync("UnreadMessageCount", groupUnreadCount, privateUnreadCount);
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
                    user.LastSeen = DateTime.Now;
                    await _context.SaveChangesAsync();
                }

                _context.UserConnections.Add(new UserConnection
                {
                    UserId = userId,
                    ConnectionId = Context.ConnectionId,
                    ConnectedAt = DateTime.Now,
                    IsActive = true
                });
                await _context.SaveChangesAsync();

                await GetUnreadMessageCount();
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = Context.User?.FindFirst("FirstName")?.Value + " " + Context.User?.FindFirst("LastName")?.Value;

            string disconnectedRoomId;
            if (_connectionRooms.TryRemove(Context.ConnectionId, out disconnectedRoomId))
            {
                if (disconnectedRoomId.ToLower() == "general")
                {
                    await Clients.Group(disconnectedRoomId).SendAsync("UserLeft", userName, userId);
                }
            }
            if (!string.IsNullOrEmpty(userId))
            {
                var connection = await _context.UserConnections
                    .FirstOrDefaultAsync(uc => uc.ConnectionId == Context.ConnectionId);

                if (connection != null)
                {
                    connection.IsActive = false;
                    await _context.SaveChangesAsync();
                }

                var hasActiveConnections = await _context.UserConnections
                    .AnyAsync(uc => uc.UserId == userId && uc.IsActive);

                if (!hasActiveConnections)
                {
                    var user = await _context.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.IsOnline = false;
                        user.LastSeen = DateTime.Now;
                        await _context.SaveChangesAsync();
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task StoreNotificationForOfflineUser(string userId, string senderName, string message)
        {
            Console.WriteLine($"Notification for offline user {userId}: New message from {senderName}");
        }
    }
}