using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Identity;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.BlockServices;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Operations view: bookings sharing a destination and check-in date, grouped into one file per
    /// supplier. The groups are derived from live booking data on every request — the only things
    /// stored about one are its status, the two supplier email links, its domestic flight documents
    /// and the snapshot of what the supplier confirmed.
    /// </summary>
    [Authorize]
    public class BlockServiceController : Controller
    {
        private const string DocumentsFolder = "BlockAttachments";

        private readonly IBlockServiceQuery _blocks;
        private readonly IBlockStatusService _status;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockExcelExporter _exporter;
        private readonly IFileStorageService _fileStorage;
        private readonly UserManager<AppUser> _userManager;

        public BlockServiceController(
            IBlockServiceQuery blocks,
            IBlockStatusService status,
            IUnitOfWork unitOfWork,
            BlockExcelExporter exporter,
            IFileStorageService fileStorage,
            UserManager<AppUser> userManager)
        {
            _blocks = blocks;
            _status = status;
            _unitOfWork = unitOfWork;
            _exporter = exporter;
            _fileStorage = fileStorage;
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
        /// Attaches domestic flight documents to an arrival group. A domestic leg is arranged per
        /// (destination, check-in), so one upload covers every booking in the group — including ones
        /// created afterwards, because the file hangs off the group rather than being copied onto each
        /// booking.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ManageDocuments)]
        [RequestSizeLimit(AttachmentRules.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = AttachmentRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadDocuments(
            int destinationId, DateOnly checkIn, List<IFormFile> files)
        {
            var accepted = (files ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();
            if (accepted.Count == 0)
            {
                TempData["Error"] = "Choose at least one file to upload.";
                return RedirectToDetails(destinationId, checkIn);
            }

            // Client-side Dropzone limits mirror these but are never trusted.
            var rejected = accepted
                .Select(f => new { f.FileName, Reason = AttachmentRules.Reject(f.FileName, f.Length) })
                .Where(x => x.Reason is not null)
                .ToList();

            if (rejected.Count > 0)
            {
                TempData["Error"] = string.Join(" ", rejected.Select(x => $"{x.FileName}: {x.Reason}"));
                return RedirectToDetails(destinationId, checkIn);
            }

            var row = await LoadOrCreateGroupAsync(destinationId, checkIn);
            var user = await _userManager.GetUserAsync(User);

            foreach (var file in accepted)
            {
                await using var stream = file.OpenReadStream();
                var stored = await _fileStorage.SaveFileAsync(stream, file.FileName, DocumentsFolder);

                await _unitOfWork.Repository<BlockAttachment>().AddAsync(new BlockAttachment
                {
                    BlockServiceStatusId = row.Id,
                    FileUrl = $"/Files/{DocumentsFolder}/{stored}",
                    OriginalFileName = file.FileName,
                    UploadedByName = user?.FullName,
                });
            }

            await _unitOfWork.CompleteAsync();

            TempData["Success"] = accepted.Count == 1
                ? "Domestic flight document uploaded."
                : $"{accepted.Count} domestic flight documents uploaded.";

            return RedirectToDetails(destinationId, checkIn);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ManageDocuments)]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var document = await _unitOfWork.Repository<BlockAttachment>()
                .GetByIdAsync(new BaseSpecifications<BlockAttachment>(a => a.Id == id));

            if (document is null)
                return Json(new { success = false, message = "Document not found." });

            // The row cascades with the group, but the file on disk doesn't — same reasoning as
            // BookingController.Delete.
            _fileStorage.DeleteFile(Path.GetFileName(document.FileUrl), DocumentsFolder);

            _unitOfWork.Repository<BlockAttachment>().Delete(document);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Document removed." });
        }

        /// <summary>
        /// The group's persisted row, created lazily — a document upload is the second thing that can
        /// bring one into existence, alongside a status change. A row created this way reads
        /// "Not booked", which is correct: attaching a ticket says nothing about the supplier.
        /// </summary>
        private async Task<BlockServiceStatus> LoadOrCreateGroupAsync(int destinationId, DateOnly checkIn)
        {
            var repository = _unitOfWork.Repository<BlockServiceStatus>();

            var row = await repository.GetByIdAsync(new BlockStatusSpec(destinationId, checkIn));
            if (row is not null) return row;

            row = new BlockServiceStatus { DestinationId = destinationId, CheckInDate = checkIn };
            await repository.AddAsync(row);
            await _unitOfWork.CompleteAsync();

            return row;
        }

        private IActionResult RedirectToDetails(int destinationId, DateOnly checkIn) =>
            RedirectToAction(nameof(Details), new
            {
                destinationId,
                checkIn = checkIn.ToString("yyyy-MM-dd"),
            });

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
