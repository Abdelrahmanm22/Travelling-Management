using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services.Dashboard;
using Travel.Presentation.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDashboardQuery _dashboard;
        private readonly IAuthorizationService _authorization;
        private readonly UserManager<AppUser> _users;

        public HomeController(IDashboardQuery dashboard, IAuthorizationService authorization, UserManager<AppUser> users)
        {
            _dashboard = dashboard;
            _authorization = authorization;
            _users = users;
        }

        /// <summary>
        /// The dashboard. No policy on the action: every signed-in user lands here, and a user without
        /// any <c>Dashboard.*</c> permission simply gets the greeting. Each section is queried only when
        /// its permission is held, so a hidden section costs nothing and its figures never render.
        /// </summary>
        public async Task<IActionResult> Index(string? period, DateOnly? from, DateOnly? to)
        {
            // UtcNow, the app-wide convention: for the first two hours after Cairo midnight "today" is
            // still yesterday, the same skew the list filters' defaults have.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var range = DashboardPeriod.Resolve(period, from, to, today);

            // Served from the change tracker afterwards — _Layout and _Nav load the same user.
            var user = await _users.GetUserAsync(User);

            var model = new DashboardViewModel
            {
                FirstName = user?.FirstName ?? User.Identity?.Name ?? string.Empty,
                Today = today,
                Period = range,

                // Sequential, not Task.WhenAll: every section shares the request's one DbContext.
                Operations = await Can(Permissions.Dashboard.ViewOperations)
                    ? await _dashboard.GetOperationsAsync(today, range.From, range.To) : null,
                Sales = await Can(Permissions.Dashboard.ViewSales)
                    ? await _dashboard.GetSalesAsync(today, range.From, range.To) : null,
                Payments = await Can(Permissions.Dashboard.ViewPayments)
                    ? await _dashboard.GetPaymentsAsync(today, range.From, range.To) : null,
                Safes = await Can(Permissions.Dashboard.ViewSafes)
                    ? await _dashboard.GetSafesAsync(range.From, range.To) : null,

                CanOpenBookings = await Can(Permissions.Bookings.View),
                CanOpenBlocks = await Can(Permissions.BlockServices.View),
                CanOpenRevenue = await Can(Permissions.Reports.ViewRevenue),
                CanOpenTransactions = await Can(Permissions.Transactions.View),
                CanOpenSafes = await Can(Permissions.MoneySafes.View),
                CanOpenExpenses = await Can(Permissions.Expenses.View),
            };

            return View(model);
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

        private async Task<bool> Can(string permission) =>
            (await _authorization.AuthorizeAsync(User, permission)).Succeeded;
    }
}
