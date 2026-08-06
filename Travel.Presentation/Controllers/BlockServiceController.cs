using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Identity;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Operations view: bookings sharing a destination and check-in date, grouped into one file per
    /// supplier. The groups are derived from live booking data on every request — the only thing
    /// stored about one is its status and the two supplier email links.
    /// </summary>
    [Authorize]
    public class BlockServiceController : Controller
    {
        private readonly IBlockServiceQuery _blocks;
        private readonly IBlockStatusService _status;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockExcelExporter _exporter;
        private readonly UserManager<AppUser> _userManager;

        public BlockServiceController(
            IBlockServiceQuery blocks,
            IBlockStatusService status,
            IUnitOfWork unitOfWork,
            BlockExcelExporter exporter,
            UserManager<AppUser> userManager)
        {
            _blocks = blocks;
            _status = status;
            _unitOfWork = unitOfWork;
            _exporter = exporter;
            _userManager = userManager;
        }

        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, bool applied = false)
            => View(await BuildListModelAsync(from, to, destinationId, status, applied));

        /// <summary>
        /// The other half of the same data: destination + check-in dates reached by exactly one
        /// booking. They need the same supplier file as a block, so they get the same
        /// <see cref="Details"/> page — they're only listed apart to keep Block Services focused.
        /// </summary>
        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> SingleArrivals(DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, bool applied = false)
            => View(await BuildListModelAsync(from, to, destinationId, status, applied));

        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> Details(int destinationId, DateOnly checkIn)
        {
            var file = await _blocks.GetBlockAsync(destinationId, checkIn);
            if (file is null)
                return NotFound();

            return View(file);
        }

        [Authorize(Policy = Permissions.BlockServices.Export)]
        public async Task<IActionResult> Export(int destinationId, DateOnly checkIn)
        {
            var file = await _blocks.GetBlockAsync(destinationId, checkIn);
            if (file is null)
                return NotFound();

            var bytes = _exporter.Build(file);
            return File(bytes, BlockExcelExporter.ContentType, BlockExcelExporter.FileName(file.Summary));
        }

        /// <summary>
        /// Records where a group stands with its supplier, and — because a booking is confirmed only
        /// when all of its groups are — recalculates every booking in it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ChangeStatus)]
        public async Task<IActionResult> SetStatus(
            int destinationId, DateOnly checkIn, BlockStatus status,
            string? requestEmailLink, string? confirmationEmailLink)
        {
            var user = await _userManager.GetUserAsync(User);

            var result = await _status.SetStatusAsync(new BlockStatusChange(
                destinationId, checkIn, status,
                requestEmailLink, confirmationEmailLink,
                user?.Id, user?.FullName));

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded
                    ? $"Block marked {StatusBadges.ForBlock(status).Label.ToLowerInvariant()}."
                    : result.Error,
            });
        }

        /// <summary>
        /// Shared by both lists so their filter semantics can't drift apart — each page renders one
        /// half of the result and links across using the other half's count.
        /// </summary>
        private async Task<BlockServiceListViewModel> BuildListModelAsync(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, bool applied)
        {
            // First visit defaults to upcoming check-ins. The filter form posts applied=true, so an
            // operator who deliberately clears the From box isn't silently pushed back to today.
            if (!applied && from is null && to is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow);

            var arrivals = await _blocks.GetArrivalsAsync(from, to, destinationId, status);

            return new BlockServiceListViewModel
            {
                From = from,
                To = to,
                DestinationId = destinationId,
                Status = status,
                Applied = applied,
                DestinationOptions = await BuildDestinationOptionsAsync(),
                Blocks = arrivals.Blocks,
                Singles = arrivals.Singles,
            };
        }

        private async Task<IEnumerable<SelectListItem>> BuildDestinationOptionsAsync()
        {
            var spec = new BaseSpecifications<Destination>(d => d.IsActive);
            spec.SetOrderBy(d => d.Name);

            var destinations = await _unitOfWork.Repository<Destination>().GetAllAsync(spec);

            return destinations
                .Select(d => new SelectListItem(d.Name, d.Id.ToString()))
                .ToList();
        }
    }
}
