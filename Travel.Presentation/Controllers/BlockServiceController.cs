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
using Travel.Domain.Services.Expenses;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.BlockServices;
using Travel.Domain.Specifications.Expenses;
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
        // Internal, not private: BookingController.Delete removes a private booking's own block files.
        internal const string DocumentsFolder = "BlockAttachments";

        private readonly IBlockServiceQuery _blocks;
        private readonly IBlockStatusService _status;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockExcelExporter _exporter;
        private readonly IFileStorageService _fileStorage;
        private readonly IExpenseChangeDetector _changes;
        private readonly UserManager<AppUser> _userManager;

        public BlockServiceController(
            IBlockServiceQuery blocks,
            IBlockStatusService status,
            IUnitOfWork unitOfWork,
            BlockExcelExporter exporter,
            IFileStorageService fileStorage,
            IExpenseChangeDetector changes,
            UserManager<AppUser> userManager)
        {
            _blocks = blocks;
            _status = status;
            _unitOfWork = unitOfWork;
            _exporter = exporter;
            _fileStorage = fileStorage;
            _changes = changes;
            _userManager = userManager;
        }

        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> Index(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, PackageCategory? category, bool applied = false)
            => View(await BuildListModelAsync(from, to, destinationId, status, category, applied));

        /// <summary>
        /// The other half of the same data: destination + check-in dates reached by exactly one
        /// booking. They need the same supplier file as a block, so they get the same
        /// <see cref="Details"/> page — they're only listed apart to keep Block Services focused.
        /// </summary>
        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> SingleArrivals(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, PackageCategory? category, bool applied = false)
            => View(await BuildListModelAsync(from, to, destinationId, status, category, applied));

        /// <summary>
        /// One arrival group. Its identity is destination + check-in + category — a 4★ and a 5★ booking
        /// on the same date are two different files — plus <paramref name="privateBookingId"/> for a
        /// private booking's own file. A link missing the category (bookmarked before the split) 404s
        /// rather than guessing which half was meant, and so does a private link whose booking has
        /// since stopped being private: nothing matches that key any more.
        /// </summary>
        [Authorize(Policy = Permissions.BlockServices.View)]
        public async Task<IActionResult> Details(
            int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId)
        {
            if (!Enum.IsDefined(category))
                return NotFound();

            var key = new BlockKey(destinationId, checkIn, category, privateBookingId);

            var file = await _blocks.GetBlockAsync(key);
            if (file is null)
                return NotFound();

            return View(new BlockDetailsViewModel
            {
                File = file,
                Expenses = await LoadExpenseLinesAsync(key),
            });
        }

        /// <summary>
        /// What this arrival group has cost, for the read-only card on Details. Screen-only — it is
        /// deliberately absent from <see cref="Export"/>, because that file goes to the hotel supplier
        /// and our costs are not theirs.
        /// </summary>
        private async Task<IReadOnlyList<BlockExpenseLine>> LoadExpenseLinesAsync(BlockKey key)
        {
            var expenses = await _unitOfWork.Repository<BlockExpense>()
                .GetAllAsync(new BlockExpenseSpec(key.DestinationId, key.CheckInDate, key.Category, key.PrivateBookingId));

            var current = await _changes.CurrentFingerprintsAsync(new[] { key });
            var live = current.GetValueOrDefault(key, string.Empty);

            return expenses
                .Select(expense =>
                {
                    var group = expense.Groups
                        .First(g => g.DestinationId == key.DestinationId
                                 && g.CheckInDate == key.CheckInDate
                                 && g.Category == key.Category
                                 && g.PrivateBookingId == key.PrivateBookingId);

                    return new BlockExpenseLine(
                        ExpenseId: expense.Id,
                        TypeName: expense.ExpenseTypeName,
                        Basis: expense.Basis,
                        SpentOn: expense.SpentOn,
                        Currency: Money.Normalize(expense.SafeEntry?.Safe?.Currency),
                        SafeName: expense.SafeEntry?.Safe?.Name ?? "—",
                        GroupAmount: group.Amount,
                        HasChanged: !ExpenseFingerprint.Matches(group.AppliedFingerprint, live));
                })
                .OrderByDescending(line => line.SpentOn)
                .ToList();
        }

        [Authorize(Policy = Permissions.BlockServices.Export)]
        public async Task<IActionResult> Export(
            int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId)
        {
            if (!Enum.IsDefined(category))
                return NotFound();

            var file = await _blocks.GetBlockAsync(new BlockKey(destinationId, checkIn, category, privateBookingId));
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
            int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId, BlockStatus status,
            string? requestEmailLink, string? confirmationEmailLink)
        {
            if (!Enum.IsDefined(category))
                return Json(new { success = false, message = "Unknown category." });

            var user = await _userManager.GetUserAsync(User);

            var result = await _status.SetStatusAsync(new BlockStatusChange(
                new BlockKey(destinationId, checkIn, category, privateBookingId), status,
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
            int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId, List<IFormFile> files)
        {
            if (!Enum.IsDefined(category))
                return NotFound();

            var key = new BlockKey(destinationId, checkIn, category, privateBookingId);

            var accepted = (files ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();
            if (accepted.Count == 0)
            {
                TempData["Error"] = "Choose at least one file to upload.";
                return RedirectToDetails(key);
            }

            // Client-side Dropzone limits mirror these but are never trusted.
            var rejected = accepted
                .Select(f => new { f.FileName, Reason = AttachmentRules.Reject(f.FileName, f.Length) })
                .Where(x => x.Reason is not null)
                .ToList();

            if (rejected.Count > 0)
            {
                TempData["Error"] = string.Join(" ", rejected.Select(x => $"{x.FileName}: {x.Reason}"));
                return RedirectToDetails(key);
            }

            var row = await LoadOrCreateGroupAsync(key);
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

            return RedirectToDetails(key);
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
            // BookingController.Delete. A file can be shared: when the 4★/5★ split divided one mixed
            // group in two (migration SplitBlocksByCategory), both halves kept a row pointing at the
            // same file. Deleting it from one half must not break the other's link.
            var sharedElsewhere = (await _unitOfWork.Repository<BlockAttachment>().GetAllAsync(
                    new BaseSpecifications<BlockAttachment>(a => a.FileUrl == document.FileUrl && a.Id != document.Id)))
                .Any();

            if (!sharedElsewhere)
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
        private async Task<BlockServiceStatus> LoadOrCreateGroupAsync(BlockKey key)
        {
            var repository = _unitOfWork.Repository<BlockServiceStatus>();

            var row = await repository.GetByIdAsync(
                new BlockStatusSpec(key.DestinationId, key.CheckInDate, key.Category, key.PrivateBookingId));
            if (row is not null) return row;

            row = new BlockServiceStatus
            {
                DestinationId = key.DestinationId,
                CheckInDate = key.CheckInDate,
                Category = key.Category,
                PrivateBookingId = key.PrivateBookingId,
            };
            await repository.AddAsync(row);
            await _unitOfWork.CompleteAsync();

            return row;
        }

        private IActionResult RedirectToDetails(BlockKey key) =>
            RedirectToAction(nameof(Details), new
            {
                destinationId = key.DestinationId,
                checkIn = key.CheckInDate.ToString("yyyy-MM-dd"),
                category = key.Category,
                privateBookingId = key.PrivateBookingId,
            });

        /// <summary>
        /// Shared by both lists so their filter semantics can't drift apart — each page renders one
        /// half of the result and links across using the other half's count.
        /// </summary>
        private async Task<BlockServiceListViewModel> BuildListModelAsync(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, PackageCategory? category, bool applied)
        {
            // First visit defaults to upcoming check-ins. The filter form posts applied=true, so an
            // operator who deliberately clears the From box isn't silently pushed back to today.
            if (!applied && from is null && to is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow);

            var arrivals = await _blocks.GetArrivalsAsync(from, to, destinationId, status, category);

            return new BlockServiceListViewModel
            {
                From = from,
                To = to,
                DestinationId = destinationId,
                Status = status,
                Category = category,
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
