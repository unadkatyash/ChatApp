using ChatApp.Data;
using ChatApp.Models;
using ChatApp.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatApp.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ChatController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string room = "general")
        {
            ViewBag.Room = room;

            var messages = await _context.ChatMessages
                .Where(m => m.RoomId == room)
                .OrderByDescending(m => m.Timestamp)
                .Take(50)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            return View(messages);
        }

        public async Task<IActionResult> PrivateChat(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Index");

            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == userId)
                return RedirectToAction("Index");

            var receiver = await _userManager.FindByIdAsync(userId);
            if (receiver == null)
                return RedirectToAction("Index");

            var messages = await _context.PrivateMessages
                .Where(pm => (pm.SenderId == currentUserId && pm.ReceiverId == userId) ||
                           (pm.SenderId == userId && pm.ReceiverId == currentUserId))
                .OrderBy(pm => pm.Timestamp)
                .Take(50)
                .Include(pm => pm.Sender)
                .Include(pm => pm.Receiver)
                .ToListAsync();

            var unreadMessages = messages.Where(m => m.ReceiverId == currentUserId && !m.IsRead).ToList();
            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.Now;
            }
            if (unreadMessages.Any())
                await _context.SaveChangesAsync();

            var onlineUsers = await GetOnlineUsersAsync();

            var viewModel = new PrivateChatViewModel
            {
                ReceiverId = userId,
                ReceiverName = $"{receiver.FirstName} {receiver.LastName}",
                Messages = messages,
                OnlineUsers = onlineUsers
            };

            return View(viewModel);
        }

        public async Task<IActionResult> ChatList()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var users = await _context.Users
                .Where(u => u.Id != currentUserId)
                .Select(u => new UserChatInfo
                {
                    UserId = u.Id,
                    FullName = u.FirstName + " " + u.LastName,
                    IsOnline = u.IsOnline,
                    LastSeen = u.LastSeen
                })
                .ToListAsync();

            foreach (var user in users)
            {
                var lastMessage = await _context.PrivateMessages
                    .Where(pm => (pm.SenderId == currentUserId && pm.ReceiverId == user.UserId) ||
                               (pm.SenderId == user.UserId && pm.ReceiverId == currentUserId))
                    .OrderByDescending(pm => pm.Timestamp)
                    .FirstOrDefaultAsync();

                if (lastMessage != null)
                {
                    user.LastMessage = lastMessage.Message.Length > 50
                        ? lastMessage.Message.Substring(0, 50) + "..."
                        : lastMessage.Message;
                    user.LastMessageTime = lastMessage.Timestamp;
                }

                user.UnreadCount = await _context.PrivateMessages
                    .CountAsync(pm => pm.SenderId == user.UserId && pm.ReceiverId == currentUserId && !pm.IsRead);
            }

            var groupMessages = await _context.ChatMessages
                .Where(m => m.RoomId == "general")
                .OrderByDescending(m => m.Timestamp)
                .Take(10)
                .ToListAsync();

            var viewModel = new ChatListViewModel
            {
                Users = users.OrderByDescending(u => u.LastMessageTime ?? DateTime.MinValue).ToList(),
                GroupMessages = groupMessages
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetOnlineUsers()
        {
            var users = await GetOnlineUsersAsync();
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var userList = users.Select(u => new
            {
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.LastSeen,
                u.IsOnline,
                IsCurrentUser = u.Id == currentUserId
            }).ToList();

            return Json(userList);
        }

        [HttpGet]
        public IActionResult GetToken()
        {
            var token = Request.Cookies["jwt"];
            return Json(new { token });
        }

        [HttpGet]
        public async Task<IActionResult> GetPrivateMessages(string userId, int page = 1, int pageSize = 20)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var messages = await _context.PrivateMessages
                .Where(pm => (pm.SenderId == currentUserId && pm.ReceiverId == userId) ||
                           (pm.SenderId == userId && pm.ReceiverId == currentUserId))
                .OrderByDescending(pm => pm.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(pm => pm.Sender)
                .Include(pm => pm.Receiver)
                .Select(pm => new
                {
                    pm.Id,
                    pm.Message,
                    pm.Timestamp,
                    pm.IsRead,
                    pm.ReadAt,
                    SenderName = pm.Sender.FirstName + " " + pm.Sender.LastName,
                    SenderId = pm.SenderId,
                    ReceiverId = pm.ReceiverId,
                    IsOwnMessage = pm.SenderId == currentUserId
                })
                .ToListAsync();

            return Json(messages.OrderBy(m => m.Timestamp));
        }

        [HttpPost]
        public async Task<IActionResult> MarkMessagesAsRead([FromBody] string senderId)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var unreadMessages = await _context.PrivateMessages
                .Where(pm => pm.SenderId == senderId && pm.ReceiverId == currentUserId && !pm.IsRead)
                .ToListAsync();

            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.Now;
            }

            if (unreadMessages.Any())
                await _context.SaveChangesAsync();

            return Json(new { success = true, count = unreadMessages.Count });
        }

        [HttpPost]
        public async Task<IActionResult> MarkMessagesAsReadGroup([FromBody] string roomId)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(roomId))
                return BadRequest(new { success = false, message = "Invalid request" });

            var unreadStatuses = await _context.MessageStatuses
                .Where(ms => ms.UserId == currentUserId &&
                             !ms.IsRead &&
                             ms.MessageType == "group" &&
                             _context.ChatMessages
                                 .Where(cm => cm.RoomId == roomId)
                                 .Select(cm => cm.Id)
                                 .Contains(ms.MessageId))
                .ToListAsync();

            foreach (var status in unreadStatuses)
            {
                status.IsRead = true;
                status.ReadAt = DateTime.UtcNow;
            }

            if (unreadStatuses.Any())
                await _context.SaveChangesAsync();

            return Json(new { success = true, count = unreadStatuses.Count });
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCounts()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var privateUnreadCount = await _context.PrivateMessages
                .CountAsync(pm => pm.ReceiverId == currentUserId && !pm.IsRead);

            var groupUnreadCount = await _context.MessageStatuses
                .CountAsync(ms => ms.UserId == currentUserId && ms.MessageType == "group" && !ms.IsRead);

            return Json(new { privateUnreadCount, groupUnreadCount });
        }

        private async Task<List<ApplicationUser>> GetOnlineUsersAsync()
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return await _context.Users
                .Where(u => u.Id != currentUserId)
                .OrderByDescending(u => u.IsOnline)
                .ThenBy(u => u.FirstName)
                .ToListAsync();
        }

    }
}