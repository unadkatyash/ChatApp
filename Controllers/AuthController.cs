using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Controllers
{
    public class AuthController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
