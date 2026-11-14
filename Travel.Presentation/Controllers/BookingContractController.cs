using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Domain.Specifications.Contracts;
using Travel.Presentation.Services.Contracts;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// A booking's client contract: the page (edit with a live preview), drafts, and the generated PDF.
    /// Gated on <see cref="Permissions.Bookings.Contract"/>, not ViewPricing — a contract inherently shows
    /// the price. A cancelled booking gets none; an unpriced one can be drafted but not generated.
    ///
    /// <para>
    /// 🔴 The CPF, RG and address arrive in <see cref="ContractRequest.Party"/>, are rendered into the
    /// PDF and then dropped: never in the draft, the audit trail or a log line (decision 3).
    /// </para>
    /// </summary>
    [Authorize(Policy = Permissions.Bookings.Contract)]
    public class BookingContractController : Controller
    {
        private const string AttachmentsFolder = "BookingAttachments";
        private const int MaxPartyFieldLength = 300;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ContractBuilder _builder;
        private readonly ContractRenderer _renderer;
        private readonly IFileStorageService _fileStorage;
        private readonly ICurrentUserService _currentUser;
        private readonly UserManager<AppUser> _userManager;
        private readonly IAuthorizationService _authorization;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BookingContractController> _logger;

        public BookingContractController(
            IUnitOfWork unitOfWork,
            ContractBuilder builder,
            ContractRenderer renderer,
            IFileStorageService fileStorage,
            ICurrentUserService currentUser,
            UserManager<AppUser> userManager,
            IAuthorizationService authorization,
            IWebHostEnvironment env,
            ILogger<BookingContractController> logger)
        {
            _unitOfWork = unitOfWork;
            _builder = builder;
            _renderer = renderer;
            _fileStorage = fileStorage;
            _currentUser = currentUser;
            _userManager = userManager;
            _authorization = authorization;
            _env = env;
            _logger = logger;
        }

        // ── The page ───────────────────────────────────────────────────────────────────────────────

        public async Task<IActionResult> Edit(int id)
        {
            var build = await _builder.BuildAsync(id);
            if (build is null) return NotFound();
            var booking = build.Booking;

            var record = await LoadRecordAsync(id);
            var draft = ContractDraft.TryDeserialize(record?.DraftJson);

            var document = draft?.Document ?? build.Document;
            var basis = draft?.Basis ?? build.Basis;
            var stale = draft is null ? Array.Empty<string>() : draft.Basis.SectionsDifferingFrom(build.Basis);

            // A loaded draft keeps its own text, so the builder's notes about a fresh schedule don't
            // describe it — only the blocking ones and the package-level ones still apply.
            var warnings = draft is null
                ? build.Warnings
                : build.Warnings.Where(w => w.Level == ContractWarning.Danger || w.Section is ContractSections.Accommodation).ToList();

            var (_, terms) = await ContractRenderer.LoadTermsAsync(_unitOfWork);

            return View(new BookingContractEditViewModel
            {
                BookingId = booking.Id,
                BookingCode = booking.Code,
                PackageId = booking.PackageId,
                PackageName = booking.Package?.Name ?? string.Empty,
                Status = booking.Status,
                IsPriced = booking.IsPriced,
                Document = document,
                Basis = basis,
                Warnings = warnings,
                StaleSections = stale,
                DraftUpdatedAt = draft is null ? null : record?.UpdatedAt,
                GeneratedAt = record?.GeneratedAt,
                GeneratedByName = record?.GeneratedByName,
                HasGeneratedPdf = booking.Attachments.Any(a => a.Category == AttachmentCategory.GeneratedContract),
                Terms = terms,
                CanEditTerms = await CanAsync(Permissions.ContractTerms.Edit),
                CanEditPackage = await CanAsync(Permissions.Packages.Edit),
                CanEditPricing = await CanAsync(Permissions.Bookings.EditPricing),
            });
        }

        // ── JSON endpoints ─────────────────────────────────────────────────────────────────────────

        /// <summary>The PDF of the current form state, for the live preview. Nothing is saved.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Preview([FromBody] ContractRequest? request)
        {
            if (request?.Document is null) return Refuse("The contract couldn't be read.", "Contract preview refused: empty request");

            var booking = await LoadBookingAsync(request.BookingId);
            if (booking is null) return Refuse("Booking not found.", "Contract preview refused: booking #{BookingId} not found", request.BookingId);
            if (booking.Status == BookingStatus.Cancelled)
                return Refuse("A cancelled booking has no contract.", "Contract preview for {BookingCode} refused: booking is cancelled", booking.Code);

            var bytes = await _renderer.RenderAsync(booking.Code, request.Document, request.Party ?? new ContractPartyDetails());
            return File(bytes, BookingContractPdf.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDraft([FromBody] ContractRequest? request)
        {
            if (request?.Document is null) return Refuse("The contract couldn't be read.", "Contract draft refused: empty request");

            var booking = await LoadBookingAsync(request.BookingId);
            if (booking is null) return Refuse("Booking not found.", "Contract draft refused: booking #{BookingId} not found", request.BookingId);
            if (booking.Status == BookingStatus.Cancelled)
                return Refuse("A cancelled booking has no contract.", "Contract draft for {BookingCode} refused: booking is cancelled", booking.Code);

            var record = await UpsertDraftAsync(booking.Id, request);
            await _unitOfWork.CompleteAsync();

            _logger.LogInformation("Contract draft saved for {BookingCode}", booking.Code);
            return Json(new { success = true, message = "Draft saved.", savedAt = record.UpdatedAt });
        }

        /// <summary>A fresh build from the booking. Nothing is saved until the operator saves or generates.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rebuild([FromBody] ContractSectionRequest? request)
        {
            var build = request is null ? null : await _builder.BuildAsync(request.BookingId);
            if (build is null) return Refuse("Booking not found.", "Contract rebuild refused: booking not found");

            return Json(new { success = true, document = build.Document, basis = build.Basis, warnings = build.Warnings });
        }

        /// <summary>One booking-driven section from a fresh build, and that section's facts.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshSection([FromBody] ContractSectionRequest? request)
        {
            if (request is null || !ContractSections.All.Contains(request.Section))
                return Refuse("Unknown section.", "Contract section refresh refused: unknown section");

            var build = await _builder.BuildAsync(request.BookingId);
            if (build is null) return Refuse("Booking not found.", "Contract section refresh refused: booking #{BookingId} not found", request.BookingId);

            return Json(new { success = true, section = request.Section, document = build.Document, basis = build.Basis });
        }

        /// <summary>
        /// Renders the final PDF, saves it on the booking (replacing the previous one), saves the draft and
        /// records the generation — the rows in one <c>SaveChanges</c>, the old file deleted only after it
        /// succeeds — then hands the PDF back as a download.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate([FromBody] ContractRequest? request)
        {
            if (request?.Document is null) return Refuse("The contract couldn't be read.", "Contract generation refused: empty request");

            // Re-read from the database: the page's disabled button is a convenience, not the rule.
            var booking = await LoadBookingAsync(request.BookingId);
            if (booking is null) return Refuse("Booking not found.", "Contract generation refused: booking #{BookingId} not found", request.BookingId);
            if (booking.Status == BookingStatus.Cancelled)
                return Refuse("A cancelled booking has no contract.", "Contract generation for {BookingCode} refused: booking is cancelled", booking.Code);
            if (!booking.IsPriced)
                return Refuse("This booking isn't fully priced. Price it on the booking before generating the contract.",
                    "Contract generation for {BookingCode} refused: booking is not priced", booking.Code);

            var party = request.Party ?? new ContractPartyDetails();
            var errors = Validate(request.Document, party);
            if (errors.Count > 0)
            {
                // Field names only — never the values, which include the CPF and RG.
                _logger.LogWarning("Contract generation for {BookingCode} rejected: {Fields}",
                    booking.Code, string.Join(", ", errors.Keys));
                return Json(new { success = false, message = "Fix the highlighted fields before generating.", errors });
            }

            var bytes = await _renderer.RenderAsync(booking.Code, request.Document, party);
            var fileName = ContractText.FileName(booking.Code);

            string storedName;
            using (var stream = new MemoryStream(bytes))
                storedName = await _fileStorage.SaveFileAsync(stream, fileName, AttachmentsFolder);

            var previous = booking.Attachments.Where(a => a.Category == AttachmentCategory.GeneratedContract).ToList();
            var user = await _userManager.GetUserAsync(User);

            try
            {
                foreach (var old in previous)
                    _unitOfWork.Repository<BookingAttachment>().Delete(old);

                await _unitOfWork.Repository<BookingAttachment>().AddAsync(new BookingAttachment
                {
                    BookingId = booking.Id,
                    Category = AttachmentCategory.GeneratedContract,
                    FileUrl = $"/Files/{AttachmentsFolder}/{storedName}",
                    OriginalFileName = fileName,
                    UploadedAt = DateTime.UtcNow,
                });

                var record = await UpsertDraftAsync(booking.Id, request);
                record.GeneratedAt = DateTime.UtcNow;
                record.GeneratedById = user?.Id ?? _currentUser.UserId;
                record.GeneratedByName = user?.FullName ?? _currentUser.UserName;

                // No detail rows: the contract carries the client's CPF, RG and address.
                await _unitOfWork.Repository<BookingAuditEvent>().AddAsync(new BookingAuditEvent
                {
                    BookingId = booking.Id,
                    OccurredAt = DateTime.UtcNow,
                    UserId = _currentUser.UserId,
                    UserName = _currentUser.UserName,
                    Action = AuditAction.ContractGenerated,
                    Summary = "Contract generated",
                });

                await _unitOfWork.CompleteAsync();
            }
            catch
            {
                _fileStorage.DeleteFile(storedName, AttachmentsFolder);
                throw;
            }

            foreach (var old in previous.Where(a => !string.IsNullOrEmpty(a.FileUrl)))
                _fileStorage.DeleteFile(Path.GetFileName(old.FileUrl!), AttachmentsFolder);

            _logger.LogInformation("Contract generated for {BookingCode}", booking.Code);
            return File(bytes, BookingContractPdf.ContentType, fileName);
        }

        /// <summary>The PDF saved by the last generation.</summary>
        public async Task<IActionResult> Download(int id)
        {
            var booking = await LoadBookingAsync(id);
            var attachment = booking?.Attachments
                .Where(a => a.Category == AttachmentCategory.GeneratedContract && !string.IsNullOrEmpty(a.FileUrl))
                .OrderByDescending(a => a.UploadedAt)
                .FirstOrDefault();
            if (attachment is null) return NotFound();

            var path = Path.Combine(_env.WebRootPath, "Files", AttachmentsFolder, Path.GetFileName(attachment.FileUrl!));
            if (!System.IO.File.Exists(path))
            {
                _logger.LogWarning("Contract for {BookingCode} missing on disk", booking!.Code);
                return NotFound();
            }

            return PhysicalFile(path, BookingContractPdf.ContentType, attachment.OriginalFileName ?? ContractText.FileName(booking!.Code));
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────────

        private async Task<Booking?> LoadBookingAsync(int bookingId) =>
            await _unitOfWork.Repository<Booking>().GetByIdAsync(new Domain.Specifications.Bookings.BookingWithDetailsSpec(bookingId));

        private async Task<BookingContract?> LoadRecordAsync(int bookingId) =>
            await _unitOfWork.Repository<BookingContract>().GetByIdAsync(new BookingContractSpec(bookingId));

        /// <summary>
        /// Writes the draft — document + the booking facts it rests on. The party details are not part of
        /// either type, so they can't reach the JSON. Not saved here; the caller completes.
        /// </summary>
        private async Task<BookingContract> UpsertDraftAsync(int bookingId, ContractRequest request)
        {
            var draft = new ContractDraft
            {
                Document = request.Document!,
                Basis = request.Basis ?? new ContractBasis(),
            };

            var record = await LoadRecordAsync(bookingId);
            if (record is null)
            {
                record = new BookingContract { BookingId = bookingId };
                await _unitOfWork.Repository<BookingContract>().AddAsync(record);
            }
            record.DraftJson = draft.Serialize();
            record.UpdatedAt = DateTime.UtcNow;
            return record;
        }

        /// <summary>Keyed by the page's field names, so each error renders next to its input.</summary>
        private static Dictionary<string, string> Validate(ContractDocument document, ContractPartyDetails party)
        {
            var errors = new Dictionary<string, string>();

            void Required(string key, string? value, string label)
            {
                if (string.IsNullOrWhiteSpace(value)) errors[key] = $"Enter the {label}.";
                else if (value.Length > MaxPartyFieldLength) errors[key] = $"The {label} is too long.";
            }

            Required("partyName", document.PartyName, "contracting party's name");
            Required("cpf", party.Cpf, "CPF");
            Required("rg", party.Rg, "RG");
            Required("address", party.Address, "address");

            if (!document.Schedule.Any(s => s.Days.Count > 0))
                errors["schedule"] = "Add at least one day to the schedule.";

            return errors;
        }

        private async Task<bool> CanAsync(string permission) =>
            (await _authorization.AuthorizeAsync(User, permission)).Succeeded;

        private IActionResult Refuse(string message, string logTemplate, params object?[] args)
        {
            _logger.LogWarning(logTemplate, args);
            return Json(new { success = false, message });
        }
    }
}
