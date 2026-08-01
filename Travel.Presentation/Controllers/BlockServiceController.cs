using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Read-only operations view: bookings sharing a destination and check-in date, grouped into one
    /// file per supplier. Everything is derived from live booking data — nothing is stored.
    /// </summary>
    [Authorize]
    public class BlockServiceController : Controller
    {
        private readonly IBlockServiceQuery _blocks;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockExcelExporter _exporter;

        public BlockServiceController(
            IBlockServiceQuery blocks,
            IUnitOfWork unitOfWork,
            BlockExcelExporter exporter)
        {
            _blocks = blocks;
            _unitOfWork = unitOfWork;
            _exporter = exporter;
        }

        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? destinationId, bool applied = false)
        {
            // First visit defaults to upcoming check-ins. The filter form posts applied=true, so an
            // operator who deliberately clears the From box isn't silently pushed back to today.
            if (!applied && from is null && to is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow);

            var model = new BlockServiceListViewModel
            {
                From = from,
                To = to,
                DestinationId = destinationId,
                Applied = applied,
                DestinationOptions = await BuildDestinationOptionsAsync(),
                Blocks = await _blocks.GetBlocksAsync(from, to, destinationId),
            };

            return View(model);
        }

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
