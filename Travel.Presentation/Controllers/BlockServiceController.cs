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
    /// stored about one are its status, the two supplier email links, its domestic flights and the
    /// snapshot of what the supplier confirmed.
    /// </summary>
    [Authorize]
    public class BlockServiceController : Controller
    {
        // Internal, not private: BookingController.Delete removes a private booking's own flight files.
        // A new folder, not the old "BlockAttachments": every row there was deleted by migration
        // DomesticFlightsPerBooking, so that folder can be removed wholesale on each server.
        internal const string FlightFilesFolder = "BlockFlights";

        private readonly IBlockServiceQuery _blocks;
        private readonly IBlockStatusService _status;
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockExcelExporter _exporter;
        private readonly IFileStorageService _fileStorage;
        private readonly IExpenseChangeDetector _changes;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<BlockServiceController> _logger;

        public BlockServiceController(
            IBlockServiceQuery blocks,
            IBlockStatusService status,
            IUnitOfWork unitOfWork,
            BlockExcelExporter exporter,
            IFileStorageService fileStorage,
            IExpenseChangeDetector changes,
            UserManager<AppUser> userManager,
            ILogger<BlockServiceController> logger)
        {
            _blocks = blocks;
            _status = status;
            _unitOfWork = unitOfWork;
            _exporter = exporter;
            _fileStorage = fileStorage;
            _changes = changes;
            _userManager = userManager;
            _logger = logger;
        }

        // How a group reads in a log line: "dest #12 2026-08-01 FiveStars", plus "private #37".
        private static string Describe(BlockKey key) =>
            $"dest #{key.DestinationId} {key.CheckInDate:yyyy-MM-dd} {key.Category}" +
            (key.PrivateBookingId is { } id ? $" private #{id}" : "");

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
            _logger.LogInformation("Block file exported: {Block}",
                Describe(new BlockKey(destinationId, checkIn, category, privateBookingId)));
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
            {
                _logger.LogWarning("Block status refused: unknown category {Category}", category);
                return Json(new { success = false, message = "Unknown category." });
            }

            var user = await _userManager.GetUserAsync(User);
            var key = new BlockKey(destinationId, checkIn, category, privateBookingId);

            var result = await _status.SetStatusAsync(new BlockStatusChange(
                key, status,
                requestEmailLink, confirmationEmailLink,
                user?.Id, user?.FullName));

            if (result.Succeeded)
                _logger.LogInformation("Block {Block} marked {Status}", Describe(key), status);
            else
                _logger.LogWarning("Block status refused for {Block} ({Status}): {Reason}", Describe(key), status, result.Error);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded
                    ? $"Block marked {StatusBadges.ForBlock(status).Label.ToLowerInvariant()}."
                    : result.Error,
            });
        }

        /// <summary>
        /// Records one domestic flight into an arrival group: number, date, time, its ticket files and
        /// the bookings on it. A booking whose stay here needs a domestic flight is covered only once a
        /// flight ticks it — see <c>BlockServiceQueryService.IsMissingFlight</c>.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ManageDocuments)]
        [RequestSizeLimit(AttachmentRules.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = AttachmentRules.MaxRequestBytes)]
        public async Task<IActionResult> AddFlight(
            int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId,
            BlockFlightInput input, List<IFormFile> files)
        {
            if (!Enum.IsDefined(category))
                return NotFound();

            var key = new BlockKey(destinationId, checkIn, category, privateBookingId);
            var block = await _blocks.GetBlockAsync(key);
            if (block is null)
                return NotFound();

            var accepted = (files ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();
            var error = ValidateFlight(input, block, accepted, keptFiles: 0);
            if (error is not null)
            {
                _logger.LogWarning("Domestic flight refused for {Block}: {Reason}", Describe(key), error);
                TempData["Error"] = error;
                return RedirectToDetails(key);
            }

            var row = await LoadOrCreateGroupAsync(key);
            var user = await _userManager.GetUserAsync(User);

            var flight = new BlockFlight
            {
                BlockServiceStatusId = row.Id,
                FlightNumber = NormalizeFlightNumber(input.FlightNumber),
                FlightDate = input.FlightDate!.Value,
                FlightTime = input.FlightTime!.Value,
                CreatedByName = user?.FullName,
                Bookings = input.BookingIds.Distinct().Select(id => new BlockFlightBooking { BookingId = id }).ToList(),
                Files = await SaveFlightFilesAsync(accepted, user?.FullName),
            };

            await _unitOfWork.Repository<BlockFlight>().AddAsync(flight);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Domestic flight #{FlightId} {FlightNumber} added to block {Block}: {Files} file(s), {Bookings} booking(s)",
                flight.Id, flight.FlightNumber, Describe(key), flight.Files.Count, flight.Bookings.Count);
            TempData["Success"] = $"Domestic flight {flight.FlightNumber} added.";

            return RedirectToDetails(key);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ManageDocuments)]
        [RequestSizeLimit(AttachmentRules.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = AttachmentRules.MaxRequestBytes)]
        public async Task<IActionResult> EditFlight(int id, BlockFlightInput input, List<IFormFile> files)
        {
            var flight = await LoadFlightAsync(id);
            if (flight is null)
                return NotFound();

            var key = KeyOf(flight.BlockServiceStatus);
            var block = await _blocks.GetBlockAsync(key);
            if (block is null)
                return NotFound();

            var removeIds = input.RemoveFileIds.ToHashSet();
            var removed = flight.Files.Where(f => removeIds.Contains(f.Id)).ToList();
            var accepted = (files ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();

            var error = ValidateFlight(input, block, accepted, keptFiles: flight.Files.Count - removed.Count);
            if (error is not null)
            {
                _logger.LogWarning("Domestic flight #{FlightId} edit refused for {Block}: {Reason}", id, Describe(key), error);
                TempData["Error"] = error;
                return RedirectToDetails(key);
            }

            var user = await _userManager.GetUserAsync(User);

            flight.FlightNumber = NormalizeFlightNumber(input.FlightNumber);
            flight.FlightDate = input.FlightDate!.Value;
            flight.FlightTime = input.FlightTime!.Value;
            flight.UpdatedAt = DateTime.UtcNow;
            flight.UpdatedByName = user?.FullName;

            // The form only shows the group's CURRENT bookings, so a tick on one that has since left
            // can't be posted either way. Leave it alone rather than let an ordinary save drop it.
            var current = block.Bookings.Select(b => b.BookingId).ToHashSet();
            var ticked = input.BookingIds.ToHashSet();

            foreach (var link in flight.Bookings.Where(l => current.Contains(l.BookingId) && !ticked.Contains(l.BookingId)).ToList())
                _unitOfWork.Repository<BlockFlightBooking>().Delete(link);

            var linked = flight.Bookings.Select(l => l.BookingId).ToHashSet();
            foreach (var bookingId in ticked.Where(b => !linked.Contains(b)))
                flight.Bookings.Add(new BlockFlightBooking { BookingId = bookingId });

            foreach (var file in removed)
                _unitOfWork.Repository<BlockAttachment>().Delete(file);

            foreach (var file in await SaveFlightFilesAsync(accepted, user?.FullName))
                flight.Files.Add(file);

            await _unitOfWork.CompleteAsync();

            // Only once the rows are gone: a failed save must not leave rows pointing at deleted files.
            foreach (var file in removed)
                _fileStorage.DeleteFile(Path.GetFileName(file.FileUrl), FlightFilesFolder);

            _logger.LogInformation("Domestic flight #{FlightId} {FlightNumber} edited on block {Block}: +{Added}/-{Removed} file(s), {Bookings} booking(s) ticked",
                flight.Id, flight.FlightNumber, Describe(key), accepted.Count, removed.Count, ticked.Count);
            TempData["Success"] = $"Domestic flight {flight.FlightNumber} updated.";

            return RedirectToDetails(key);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.BlockServices.ManageDocuments)]
        public async Task<IActionResult> DeleteFlight(int id)
        {
            var flight = await LoadFlightAsync(id);
            if (flight is null)
            {
                _logger.LogWarning("Delete refused: domestic flight #{FlightId} not found", id);
                return Json(new { success = false, message = "Flight not found." });
            }

            var files = flight.Files.ToList();
            var key = KeyOf(flight.BlockServiceStatus);

            // The files and booking ticks cascade as rows; the files on disk don't.
            _unitOfWork.Repository<BlockFlight>().Delete(flight);
            await _unitOfWork.CompleteAsync();

            foreach (var file in files)
                _fileStorage.DeleteFile(Path.GetFileName(file.FileUrl), FlightFilesFolder);

            _logger.LogInformation("Domestic flight #{FlightId} {FlightNumber} deleted from block {Block} ({Files} file(s))",
                flight.Id, flight.FlightNumber, Describe(key), files.Count);

            return Json(new { success = true, message = $"Domestic flight {flight.FlightNumber} deleted." });
        }

        private const int MaxFlightNumberLength = 20;

        /// <summary>
        /// Every rule a flight must satisfy, or the first message to show. A tick can only name a
        /// booking currently in the group: anything else is a stale page or a tampered post.
        /// </summary>
        private static string? ValidateFlight(BlockFlightInput input, BlockFile block, List<IFormFile> newFiles, int keptFiles)
        {
            var number = NormalizeFlightNumber(input.FlightNumber);
            if (number.Length == 0) return "Enter the flight number.";
            if (number.Length > MaxFlightNumberLength) return $"The flight number can be at most {MaxFlightNumberLength} characters.";
            if (input.FlightDate is null) return "Enter the flight date.";
            if (input.FlightTime is null) return "Enter the flight time.";

            if (input.BookingIds.Count == 0) return "Tick at least one booking on this flight.";
            var current = block.Bookings.Select(b => b.BookingId).ToHashSet();
            if (input.BookingIds.Any(id => !current.Contains(id)))
                return "One of the ticked bookings is no longer in this arrival. Reload the page and try again.";

            if (keptFiles + newFiles.Count == 0) return "Attach at least one ticket file.";

            // Reasons only: a ticket's file name often carries the passenger's name, and this message
            // also lands in the log line.
            var reasons = newFiles
                .Select(f => AttachmentRules.Reject(f.FileName, f.Length))
                .Where(r => r is not null)
                .Distinct()
                .ToList();
            return reasons.Count > 0 ? "Some files were rejected: " + string.Join(" ", reasons) : null;
        }

        private static string NormalizeFlightNumber(string? value) =>
            (value ?? string.Empty).Trim().ToUpperInvariant();

        private async Task<List<BlockAttachment>> SaveFlightFilesAsync(List<IFormFile> files, string? uploadedBy)
        {
            var saved = new List<BlockAttachment>();
            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                var stored = await _fileStorage.SaveFileAsync(stream, file.FileName, FlightFilesFolder);
                saved.Add(new BlockAttachment
                {
                    FileUrl = $"/Files/{FlightFilesFolder}/{stored}",
                    OriginalFileName = file.FileName,
                    UploadedByName = uploadedBy,
                });
            }
            return saved;
        }

        private async Task<BlockFlight?> LoadFlightAsync(int id)
        {
            var spec = new BaseSpecifications<BlockFlight>(f => f.Id == id);
            spec.AddInclude("BlockServiceStatus");
            spec.AddInclude("Files");
            spec.AddInclude("Bookings");
            // One flight: a few files x a few ticks.
            spec.UseSingleQuery();
            return await _unitOfWork.Repository<BlockFlight>().GetByIdAsync(spec);
        }

        private static BlockKey KeyOf(BlockServiceStatus row) =>
            new(row.DestinationId, row.CheckInDate, row.Category, row.PrivateBookingId);

        /// <summary>
        /// The group's persisted row, created lazily — adding a domestic flight is the second thing that
        /// can bring one into existence, alongside a status change. A row created this way reads
        /// "Not booked", which is correct: recording a ticket says nothing about the supplier.
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
