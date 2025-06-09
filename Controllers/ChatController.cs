using ChatApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers
{
    //[Authorize]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ChatController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string room = "general")
        {
            ViewBag.Room = room;

            var messages = await _context.ChatMessages
                .Where(m => m.RoomId == room)
                .OrderBy(m => m.Timestamp)
                .Take(50)
                .ToListAsync();

            return View(messages);
        }

        [HttpGet]
        public async Task<IActionResult> GetOnlineUsers()
        {
            var users = await _context.Users
                .Where(u => u.IsOnline)
                .Select(u => new
                {
                    u.UserName,
                    u.FirstName,
                    u.LastName
                })
                .ToListAsync();

            return Json(users);
        }

        [HttpGet]
        public IActionResult GetToken()
        {
            var token = Request.Cookies["jwt"];
            return Json(new { token });
        }
    }
}
