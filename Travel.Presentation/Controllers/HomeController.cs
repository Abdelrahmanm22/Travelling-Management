using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Presentation.Models;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // /Home/Error is the UseExceptionHandler target — requiring auth here would redirect an
        // anonymous user to Login and swallow the real error.
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // TraceIdentifier, not Activity.Current.Id: it is the RequestId stamped on every log line,
            // so the id a user reads off this page is the one to grep for.
            return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
        }
    }
}
