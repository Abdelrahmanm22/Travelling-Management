using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Exceptions;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.CodeGeneration;
using Travel.Domain.Services.Itinerary;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Bookings;
using Travel.Domain.Specifications.Packages;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private const string AttachmentsFolder = "BookingAttachments";

        /// <summary>Shared with TransactionController — the same folder holds every receipt.</summary>
        private const string TransactionReceiptsFolder = "TransactionAttachments";

        // Extension/size rules and the request-body cap live in AttachmentRules — shared with
        // BlockServiceController so the two upload paths can't disagree on what a valid file is.
        private const long MaxAttachmentsRequestBytes = AttachmentRules.MaxRequestBytes;

        /// <summary>
        /// What a user without <c>Bookings.EditPricing</c> must not be able to change on a stay row:
        /// the room rates and whether the guest arranged the nights. Both are carried forward from
        /// storage rather than read from their post.
        /// </summary>
        private sealed record StayPricing(bool ArrangedByGuest, List<BookingStayRate> Rates);

        /// <summary>"Preserve nothing" — passed to BuildStaysFromInputsAsync on Create, where a user
        /// who can't price has no stored rate to carry forward and must not set one either.</summary>
        private static readonly IReadOnlyDictionary<int, StayPricing> EmptyPrices =
            new Dictionary<int, StayPricing>();

        /// <summary>The same for tours: on Create there are no stored rows to carry forward.</summary>
        private static readonly IReadOnlyList<BookingTour> EmptyTours = new List<BookingTour>();

        /// <summary>
        /// The deposit taken as the booking is filed. Written after the code generator has saved the
        /// booking (its id doesn't exist before that), so this is a second CompleteAsync — which is
        /// exactly why <see cref="Create"/> is the one create path where the two saves are worth
        /// keeping adjacent rather than interleaved with the block recalculation.
        /// </summary>
        private async Task RecordFirstPaymentAsync(Booking booking, BookingViewModel model)
        {
            var transaction = new BookingTransaction
            {
                BookingId = booking.Id,
                Type = TransactionType.Payment,
                Method = model.FirstPaymentMethod,
                Amount = model.FirstPaymentAmount!.Value,
                PaidOn = model.FirstPaymentDate,
                Reference = string.IsNullOrWhiteSpace(model.FirstPaymentReference) ? null : model.FirstPaymentReference.Trim(),
                Notes = string.IsNullOrWhiteSpace(model.FirstPaymentNotes) ? null : model.FirstPaymentNotes.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedById = _currentUser.UserId,
                CreatedByName = _currentUser.UserName,
            };

            foreach (var file in model.FirstPaymentFiles.Where(f => f.Length > 0))
            {
                await using var stream = file.OpenReadStream();
                var storedName = await _fileStorage.SaveFileAsync(stream, file.FileName, TransactionReceiptsFolder);
                transaction.Attachments.Add(new TransactionAttachment
                {
                    FileUrl = $"/Files/{TransactionReceiptsFolder}/{storedName}",
                    OriginalFileName = file.FileName,
                    UploadedAt = DateTime.UtcNow,
                });
            }

            await _unitOfWork.Repository<BookingTransaction>().AddAsync(transaction);
        }

        private async Task RecordCreatedAuditAsync(Booking booking)
        {
            await _unitOfWork.Repository<BookingAuditEvent>().AddAsync(new BookingAuditEvent
            {
                BookingId = booking.Id,
                OccurredAt = DateTime.UtcNow,
                UserId = _currentUser.UserId,
                UserName = _currentUser.UserName,
                Action = AuditAction.Created,
                Summary = $"Booking {booking.Code} created",
            });
            await _unitOfWork.CompleteAsync();
        }

        /// <summary>Newest first, with the field-level changes eager-loaded for the Details card.</summary>
        private async Task<IReadOnlyList<BookingAuditEvent>> LoadAuditAsync(int bookingId)
        {
            var spec = new BaseSpecifications<BookingAuditEvent>(e => e.BookingId == bookingId);
            spec.AddInclude("Changes");
            spec.SetOrderByDesc(e => e.OccurredAt);
            return (await _unitOfWork.Repository<BookingAuditEvent>().GetAllAsync(spec)).ToList();
        }

        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IItineraryGeneratorService _generator;
        private readonly ICodeGeneratorService _codes;
        private readonly IFileStorageService _fileStorage;
        private readonly BookingExcelExporter _excel;
        private readonly IBlockStatusService _blockStatus;
        private readonly IAuthorizationService _authorization;
        private readonly ICurrentUserService _currentUser;

        public BookingController(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IItineraryGeneratorService generator,
            ICodeGeneratorService codes,
            IFileStorageService fileStorage,
            BookingExcelExporter excel,
            IBlockStatusService blockStatus,
            IAuthorizationService authorization,
            ICurrentUserService currentUser)
        {
            _currentUser = currentUser;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _generator = generator;
            _codes = codes;
            _fileStorage = fileStorage;
            _excel = excel;
            _blockStatus = blockStatus;
            _authorization = authorization;
        }

        /// <summary>
        /// Pricing is gated separately from the booking itself, so the answer is needed as a value —
        /// not as an all-or-nothing [Authorize] — to render the same page with and without money.
        /// </summary>
        private async Task<bool> CanAsync(string policy) =>
            (await _authorization.AuthorizeAsync(User, policy)).Succeeded;

        [Authorize(Policy = Permissions.Bookings.View)]
        public async Task<IActionResult> Index(DateOnly? arrivalFrom, DateOnly? arrivalTo, int? packageId,
                                               BookingStatus? status, bool guestArranged = false)
        {
            var bookings = await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new BookingWithDetailsSpec(arrivalFrom, arrivalTo, packageId, status, guestArranged));

            return View(new BookingListViewModel
            {
                ArrivalFrom = arrivalFrom,
                ArrivalTo = arrivalTo,
                PackageId = packageId,
                Status = status,
                GuestArranged = guestArranged,
                PackageOptions = await BuildPackageFilterOptionsAsync(),
                Bookings = bookings.ToList(),
                CanViewPricing = await CanAsync(Permissions.Bookings.ViewPricing),
                CanViewTransactions = await CanAsync(Permissions.Transactions.View),
            });
        }

        [Authorize(Policy = Permissions.Bookings.View)]
        public async Task<IActionResult> Details(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            var canViewAudit = await CanAsync(Permissions.Bookings.ViewAudit);

            return View(new BookingDetailsViewModel
            {
                Booking = booking,
                // Why this booking is (or isn't) confirmed — one line per arrival group it belongs to.
                Blocks = await _blockStatus.GetForBookingAsync(booking.Id),
                CanViewPricing = await CanAsync(Permissions.Bookings.ViewPricing),
                CanViewTransactions = await CanAsync(Permissions.Transactions.View),
                CanCreateTransactions = await CanAsync(Permissions.Transactions.Create),
                CanEditTransactions = await CanAsync(Permissions.Transactions.Edit),
                CanDeleteTransactions = await CanAsync(Permissions.Transactions.Delete),
                CanViewAudit = canViewAudit,
                AuditEvents = canViewAudit ? await LoadAuditAsync(booking.Id) : new List<BookingAuditEvent>(),
            });
        }

        [Authorize(Policy = Permissions.Bookings.Export)]
        public async Task<IActionResult> Export(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            // The same button yields a priced sheet for a sales agent and an unpriced one for everyone else.
            var bytes = _excel.Build(booking, includePricing: await CanAsync(Permissions.Bookings.ViewPricing));
            return File(bytes, BookingExcelExporter.ContentType, BookingExcelExporter.FileName(booking));
        }

        [Authorize(Policy = Permissions.Bookings.Create)]
        public async Task<IActionResult> Create()
        {
            var model = new BookingViewModel
            {
                ArrivalDate = DateOnly.FromDateTime(DateTime.Today),
                Adults = 1,
                Guests = { new BookingGuestInput { Type = GuestType.Adult } },
                Rooms = { new BookingRoomInput { Type = RoomType.Double, Quantity = 1 } },
            };
            await PopulateFormAsync(model, package: null);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Bookings.Create)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Create(BookingViewModel model)
        {
            var package = await LoadPackageAsync(model.PackageId);
            // The form generates the schedule client-side and posts it back. An empty one means the
            // editor never ran (JS blocked, stale page) — fall back to generating it here.
            var hasPostedStays = model.Stays is { Count: > 0 };

            var canEditPricing = await CanAsync(Permissions.Bookings.EditPricing);
            var canRecordPayments = await CanAsync(Permissions.Transactions.Create);

            ValidateBooking(model, package, isCreate: true);
            if (hasPostedStays)
                ValidateStaySchedule(model);
            ValidateGuestsAndRooms(model);
            ValidateAttachments(model);
            ValidateAttachmentFiles(model.FirstPaymentFiles, nameof(model.FirstPaymentFiles), AttachmentRules.MaxReceiptFiles);
            if (canEditPricing)
                ValidatePricing(model);
            if (canRecordPayments)
                ValidateFirstPayment(model, PostedGrandTotal(model));

            if (!ModelState.IsValid)
            {
                await PopulateFormAsync(model, package);
                return View(model);
            }

            var booking = _mapper.Map<Booking>(model);
            booking.CreatedAt = DateTime.UtcNow;
            if (canEditPricing)
                ApplyPricing(booking, model);

            if (hasPostedStays)
            {
                // The operator's table is the source of truth — same handling as Edit. There is nothing
                // stored yet to preserve, so a user who can't price simply creates unpriced stays.
                booking.Stays = await BuildStaysFromInputsAsync(
                    model.Stays, canEditPricing ? null : EmptyPrices);
                var scheduled = booking.Stays.Sum(s => s.Nights);
                booking.DepartureDate = model.ArrivalDate.AddDays(scheduled);
                // Loses the generator's cruise-specific wording for the extension reason; Edit does
                // the same for any extension introduced after the fact.
                ApplyExtraNights(booking, package!, scheduled);
                booking.EmbarkDay = DeriveEmbarkFromStays(booking.Stays) ?? ResolveStoredEmbark(package!, model);
                booking.IsManuallyEdited = model.IsManuallyEdited;
            }
            else
            {
                booking.IsManuallyEdited = false;
                booking.EmbarkDay = ResolveStoredEmbark(package!, model);
                // Needed by the generator for variable-length packages; overwritten below for fixed ones.
                booking.DepartureDate = model.DepartureDate;

                GeneratedItinerary itinerary;
                try
                {
                    itinerary = GenerateFor(package!, booking);
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    await PopulateFormAsync(model, package);
                    return View(model);
                }

                booking.DepartureDate = itinerary.DepartureDate;
                booking.ExtraNights = itinerary.ExtraNights;
                booking.ExtensionReason = itinerary.ExtensionReason;
                booking.Stays = BuildStaysFromGenerated(itinerary);
            }

            booking.Guests = BuildGuests(model);
            booking.Rooms = BuildRooms(model);
            booking.Tours = BuildTours(model, canEditPricing ? null : EmptyTours);
            booking.Attachments = await BuildNewAttachmentsAsync(model);
            if (!string.IsNullOrWhiteSpace(model.ContractLink))
            {
                booking.Attachments.Add(new BookingAttachment
                {
                    Category = AttachmentCategory.Contract,
                    LinkUrl = model.ContractLink.Trim(),
                });
            }

            try
            {
                await _codes.AddWithCodeAsync(booking, CodePrefixes.Booking);
            }
            catch (DuplicateCodeException)
            {
                ModelState.AddModelError(string.Empty,
                    "Another booking was created at the same moment. Please submit again.");
                await PopulateFormAsync(model, package);
                return View(model);
            }

            // The booking exists now, so its id is available for the first payment.
            if (canRecordPayments && model.FirstPaymentAmount is > 0m)
                await RecordFirstPaymentAsync(booking, model);

            // Its stays decide which blocks it belongs to, so its status follows from them — a booking
            // created into an already-confirmed set of blocks lands Confirmed.
            await _blockStatus.RecalculateAsync(new[] { booking.Id });
            await _unitOfWork.CompleteAsync();

            // Written here rather than by the interceptor: a new booking has no id until SaveChanges
            // returns, so the interceptor has nothing to key its changes to and skips Added bookings.
            await RecordCreatedAuditAsync(booking);

            TempData["Success"] = $"Booking {booking.Code} created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Bookings.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            var model = _mapper.Map<BookingViewModel>(booking);
            model.ContractLink = booking.Attachments
                .FirstOrDefault(a => a.Category == AttachmentCategory.Contract)?.LinkUrl;
            model.ExistingAttachments = ToAttachmentViewModels(booking.Attachments);
            var package = await LoadPackageAsync(booking.PackageId);
            await PopulateFormAsync(model, package);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Bookings.Edit)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Edit(BookingViewModel model)
        {
            var package = await LoadPackageAsync(model.PackageId);
            var canEditPricing = await CanAsync(Permissions.Bookings.EditPricing);

            ValidateBooking(model, package, isCreate: false);
            ValidateStaySchedule(model);
            ValidateGuestsAndRooms(model);
            ValidateAttachments(model);
            if (canEditPricing)
                ValidatePricing(model);

            if (!ModelState.IsValid)
            {
                var existing = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(model.Id));
                if (existing is not null)
                    model.ExistingAttachments = ToAttachmentViewModels(existing.Attachments);
                await PopulateFormAsync(model, package);
                return View(model);
            }

            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(model.Id));
            if (booking is null)
                return NotFound();

            _mapper.Map(model, booking); // scalars only (Stays/DepartureDate/Extra*/prices ignored by the map)
            if (canEditPricing)
                ApplyPricing(booking, model);

            // Replace stays with the operator's edited schedule (source of truth). Capture the stored
            // extra-night rates first — a user without EditPricing never saw them, so their post can't
            // carry them and the rebuild would otherwise drop them.
            // Keyed by POSITION, matching how BuildStaysFromInputsAsync looks it up. SortOrder is
            // re-derived as the position on every save, so the two agree — but keying on SortOrder
            // here and reading by index there was an accident waiting to diverge.
            var keptStayPrices = canEditPricing
                ? null
                : booking.Stays.OrderBy(s => s.SortOrder)
                               .Select((s, i) => (i, Pricing: new StayPricing(
                                   s.ExtraNightsArrangedByGuest, s.Rates.ToList())))
                               .ToDictionary(x => x.i, x => x.Pricing);

            foreach (var old in booking.Stays.ToList())
                _unitOfWork.Repository<BookingStay>().Delete(old);
            booking.Stays = await BuildStaysFromInputsAsync(model.Stays, keptStayPrices);
            // Same rule as Create: the cruise row's own check-in is the sailing the guest boards,
            // falling back to the posted picker. Returns null for a non-cruise package, so switching
            // a cruise booking to one can't leave a stale weekday behind.
            booking.EmbarkDay = DeriveEmbarkFromStays(booking.Stays) ?? ResolveStoredEmbark(package!, model);

            // Same for the roster and rooming — rebuilt wholesale from the posted rows.
            foreach (var guest in booking.Guests.ToList())
                _unitOfWork.Repository<BookingGuest>().Delete(guest);
            booking.Guests = BuildGuests(model);

            foreach (var room in booking.Rooms.ToList())
                _unitOfWork.Repository<BookingRoom>().Delete(room);
            booking.Rooms = BuildRooms(model);

            // Same preserve-what-they-can't-see rule as the stay rates above — and here it has to be
            // the whole row, not just the price: the card isn't rendered for them at all.
            var keptTours = canEditPricing
                ? null
                : booking.Tours.OrderBy(t => t.SortOrder).ToList();

            foreach (var tour in booking.Tours.ToList())
                _unitOfWork.Repository<BookingTour>().Delete(tour);
            booking.Tours = BuildTours(model, keptTours);

            var scheduled = booking.Stays.Sum(s => s.Nights);
            booking.DepartureDate = model.ArrivalDate.AddDays(scheduled);
            ApplyExtraNights(booking, package!, scheduled);

            // Attachments are additive, not wholesale-rebuilt — files can't be regenerated like Stays/Guests/Rooms.
            foreach (var id in model.RemoveAttachmentIds)
            {
                var attachment = booking.Attachments.FirstOrDefault(a => a.Id == id);
                if (attachment is null)
                    continue;

                if (!string.IsNullOrEmpty(attachment.FileUrl))
                    _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), AttachmentsFolder);
                booking.Attachments.Remove(attachment);
                _unitOfWork.Repository<BookingAttachment>().Delete(attachment);
            }

            foreach (var newAttachment in await BuildNewAttachmentsAsync(model))
                booking.Attachments.Add(newAttachment);

            var contract = booking.Attachments.FirstOrDefault(a => a.Category == AttachmentCategory.Contract);
            if (string.IsNullOrWhiteSpace(model.ContractLink))
            {
                if (contract is not null)
                {
                    booking.Attachments.Remove(contract);
                    _unitOfWork.Repository<BookingAttachment>().Delete(contract);
                }
            }
            else if (contract is not null)
            {
                contract.LinkUrl = model.ContractLink.Trim();
            }
            else
            {
                booking.Attachments.Add(new BookingAttachment
                {
                    Category = AttachmentCategory.Contract,
                    LinkUrl = model.ContractLink.Trim(),
                });
            }

            _unitOfWork.Repository<Booking>().Update(booking);
            await _unitOfWork.CompleteAsync();

            // The edited schedule may have moved the booking into or out of a block.
            await _blockStatus.RecalculateAsync(new[] { booking.Id });
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Booking updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Bookings.Regenerate)]
        public async Task<IActionResult> Regenerate(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            var package = await LoadPackageAsync(booking.PackageId);
            if (package is null)
            {
                TempData["Error"] = "The booking's package no longer exists.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            GeneratedItinerary itinerary;
            try
            {
                itinerary = GenerateFor(package, booking);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = "Couldn't regenerate the schedule: " + ex.Message;
                return RedirectToAction(nameof(Edit), new { id });
            }

            foreach (var old in booking.Stays.ToList())
                _unitOfWork.Repository<BookingStay>().Delete(old);
            booking.Stays = BuildStaysFromGenerated(itinerary);
            // The generator falls back to the soonest sailing when the stored day is stale; without
            // this the row and the stored EmbarkDay would disagree until the next Edit save.
            booking.EmbarkDay = DeriveEmbarkFromStays(booking.Stays) ?? booking.EmbarkDay;
            booking.DepartureDate = itinerary.DepartureDate;
            booking.ExtraNights = itinerary.ExtraNights;
            booking.ExtensionReason = itinerary.ExtensionReason;
            booking.IsManuallyEdited = false;

            _unitOfWork.Repository<Booking>().Update(booking);
            await _unitOfWork.CompleteAsync();

            // A regenerated schedule can land on different destinations or dates than the old one.
            await _blockStatus.RecalculateAsync(new[] { booking.Id });
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Schedule regenerated from the package.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        /// <summary>
        /// The operator-owned half of the booking lifecycle. <see cref="BookingStatus.InProgress"/> and
        /// <see cref="BookingStatus.Confirmed"/> are deliberately not settable here — they are derived
        /// from the statuses of the blocks the booking belongs to. "Reactivate" hands a completed or
        /// cancelled booking back to that derivation rather than picking a value.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Bookings.ChangeStatus)]
        public async Task<IActionResult> SetStatus(int id, BookingStatus? status)
        {
            if (status is not (null or BookingStatus.Completed or BookingStatus.Cancelled))
                return Json(new
                {
                    success = false,
                    message = "In progress and confirmed are set by Block Services, not by hand.",
                });

            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingsForStatusSpec(id));
            if (booking is null)
                return Json(new { success = false, message = "Booking not found." });

            if (status is null)
            {
                // Reactivate: recompute from the blocks instead of assuming In progress.
                booking.Status = BookingStatus.InProgress;
                _unitOfWork.Repository<Booking>().Update(booking);
                await _blockStatus.RecalculateAsync(new[] { booking.Id });
            }
            else
            {
                booking.Status = status.Value;
                _unitOfWork.Repository<Booking>().Update(booking);
            }

            await _unitOfWork.CompleteAsync();

            var label = StatusBadges.ForBooking(booking.Status).Label.ToLowerInvariant();
            return Json(new { success = true, message = $"Booking {booking.Code} is now {label}." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // View, not Create — both the Create and Edit forms call this.
        [Authorize(Policy = Permissions.Bookings.View)]
        public async Task<IActionResult> Preview(int packageId, DateOnly? arrivalDate, DateOnly? departureDate, WeekDays? embarkDay)
        {
            if (arrivalDate is null)
                return Json(new { success = false, error = "Select an arrival date." });

            var package = await LoadPackageAsync(packageId);
            if (package is null)
                return Json(new { success = false, error = "Package not found." });
            if (!package.IsActive)
                return Json(new { success = false, error = "This package is inactive." });

            var arrival = arrivalDate.Value;
            var hasCruise = package.Segments.Any(s => s.Destination.IsCruise);
            if (hasCruise && package.Segments.Any(s => s.Destination.IsCruise && s.Destination.DepartureDays is null))
                return Json(new { success = false, error = "This package's cruise has no departure days set. Configure them on the Destination first." });

            var hasFixed = package.TotalNights.HasValue;
            if (!hasFixed && (departureDate is null || departureDate <= arrival))
                return Json(new { success = false, error = "Enter a departure date after the arrival date." });

            GeneratedItinerary itinerary;
            try
            {
                itinerary = _generator.Generate(package, arrival, embarkDay, hasFixed ? null : departureDate);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var names = BuildDestinationLookup(package);
            var stays = itinerary.Stays.Select(s => new
            {
                destinationId = s.DestinationId,
                destinationName = names.TryGetValue(s.DestinationId, out var n) ? n : "",
                isCruise = s.IsCruise,
                checkIn = s.CheckIn.ToString("yyyy-MM-dd"),
                checkOut = s.CheckIn.AddDays(s.Nights).ToString("yyyy-MM-dd"),
                nights = s.Nights,
                // Part of the JSON contract with renderRows() — without it the client would fall back
                // to the template's 0 and every generated night would read as a chargeable extra.
                baselineNights = s.BaselineNights,
                sortOrder = s.SortOrder,
            });

            var embarkOptions = _generator.GetEmbarkOptions(package, arrival);
            // Same factory PopulateFormAsync uses, so the server-rendered radios and the ones
            // renderEmbark() rebuilds from this response can't disagree on the label text.
            // The JS reads value/text/extraNights — MVC's default camelCase policy supplies those names.
            var embarkVms = EmbarkOptionViewModel.Build(embarkOptions, package.BaseDestination.Name);
            return Json(new
            {
                success = true,
                hasFixedTotal = hasFixed,
                hasCruise,
                // Banner config — the Create form has no package context until this call returns.
                packageTotalNights = package.TotalNights,
                baseDestinationId = package.BaseDestinationId,
                departureDate = itinerary.DepartureDate.ToString("yyyy-MM-dd"),
                extraNights = itinerary.ExtraNights,
                extensionReason = itinerary.ExtensionReason,
                embarkOptions = embarkVms,
                defaultEmbark = embarkOptions.Count > 0 ? embarkOptions[0].Day.ToString() : null,
                stays,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Bookings.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return Json(new { success = false, message = "Booking not found." });

            foreach (var attachment in booking.Attachments.Where(a => !string.IsNullOrEmpty(a.FileUrl)))
                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl!), AttachmentsFolder);

            // The DB cascade removes the transaction and attachment rows, never the receipts on disk.
            // Loaded with their own spec rather than off booking.Transactions: an empty navigation here
            // leaves orphaned files with nothing pointing at them, and this must not silently depend on
            // BookingWithDetailsSpec keeping its Transactions.Attachments include.
            var receipts = await _unitOfWork.Repository<TransactionAttachment>()
                .GetAllAsync(new BaseSpecifications<TransactionAttachment>(a => a.Transaction.BookingId == id));

            foreach (var receipt in receipts)
                _fileStorage.DeleteFile(Path.GetFileName(receipt.FileUrl), TransactionReceiptsFolder);

            // The confirmation snapshots this booking appears in are removed by hand: their FK is
            // NoAction, which is what breaks the second cascade path into that table (error 1785).
            var snapshots = await _unitOfWork.Repository<BlockRecordedBooking>()
                .GetAllAsync(new BaseSpecifications<BlockRecordedBooking>(c => c.BookingId == id));

            foreach (var snapshot in snapshots)
                _unitOfWork.Repository<BlockRecordedBooking>().Delete(snapshot);

            // A private booking's own block records (status, links, documents) belong to it alone, and
            // carry no FK back to it — a second path into Bookings would trip error 1785 — so they are
            // removed by hand. Their documents cascade as rows but not as files on disk.
            var privateBlocksSpec = new BaseSpecifications<BlockServiceStatus>(s => s.PrivateBookingId == id);
            privateBlocksSpec.AddInclude("Attachments");
            var privateBlocks = (await _unitOfWork.Repository<BlockServiceStatus>().GetAllAsync(privateBlocksSpec)).ToList();

            foreach (var block in privateBlocks)
            {
                foreach (var document in block.Attachments)
                {
                    // Same guard as BlockServiceController.DeleteDocument: never remove a file that
                    // another block's row still points at.
                    var shared = (await _unitOfWork.Repository<BlockAttachment>().GetAllAsync(
                            new BaseSpecifications<BlockAttachment>(a => a.FileUrl == document.FileUrl
                                                                     && a.BlockServiceStatusId != block.Id)))
                        .Any();

                    if (!shared)
                        _fileStorage.DeleteFile(Path.GetFileName(document.FileUrl), BlockServiceController.DocumentsFolder);
                }

                _unitOfWork.Repository<BlockServiceStatus>().Delete(block);
            }

            // Expense allocations point at this booking with a NoAction FK, for the same reason. They
            // are NULLED, not deleted: each line records money that already left a safe, and dropping
            // one would make its group's stored amount stop matching the sum of its own visible lines,
            // with nothing anywhere to say why. BookingCode keeps the line readable afterwards.
            var allocations = await _unitOfWork.Repository<BlockExpenseBooking>()
                .GetAllAsync(new BaseSpecifications<BlockExpenseBooking>(a => a.BookingId == id));

            foreach (var allocation in allocations)
            {
                allocation.BookingId = null;
                _unitOfWork.Repository<BlockExpenseBooking>().Update(allocation);
            }

            _unitOfWork.Repository<Booking>().Delete(booking);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Booking deleted successfully." });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private async Task<Package?> LoadPackageAsync(int id) =>
            id <= 0 ? null : await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageWithDetailsSpec(id));

        private GeneratedItinerary GenerateFor(Package package, Booking booking) =>
            _generator.Generate(package, booking.ArrivalDate, booking.EmbarkDay,
                package.TotalNights.HasValue ? null : booking.DepartureDate);

        // Stored for every cruise booking; falls back to the soonest sailing when the posted day isn't valid.
        private WeekDays? ResolveStoredEmbark(Package package, BookingViewModel model)
        {
            if (!package.Segments.Any(s => s.Destination.IsCruise))
                return null;

            var options = _generator.GetEmbarkOptions(package, model.ArrivalDate);
            if (options.Count == 0)
                return null;

            return options.Any(o => o.Day == model.EmbarkDay) ? model.EmbarkDay : options[0].Day;
        }

        // Keeps the stored embark day honest once the schedule is hand-edited: the cruise stay's own
        // check-in is the sailing the guest actually boards. A no-op on an untouched schedule, since
        // the generator sets that check-in to the chosen option's date.
        // WeekDays' bit layout is 1 << DayOfWeek — Sunday=1, Monday=2 … Saturday=64.
        private static WeekDays? DeriveEmbarkFromStays(IEnumerable<BookingStay> stays)
        {
            var cruise = stays.FirstOrDefault(s => s.IsCruise);
            return cruise is null ? null : (WeekDays)(1 << (int)cruise.CheckInDate.DayOfWeek);
        }

        private List<BookingStay> BuildStaysFromGenerated(GeneratedItinerary itinerary) =>
            itinerary.Stays.OrderBy(s => s.SortOrder).Select(s => _mapper.Map<BookingStay>(s)).ToList();

        /// <param name="preservedPrices">
        /// Stay pricing keyed by <b>position</b>, supplied when the user lacks Bookings.EditPricing.
        /// Edit rebuilds Stays wholesale, and the rate panel isn't rendered for that user — so without
        /// this their ordinary save would silently wipe every extra-night rate on the booking.
        /// Null means "trust the posted values", i.e. the user may price.
        /// </param>
        private async Task<List<BookingStay>> BuildStaysFromInputsAsync(
            List<BookingStayInput> inputs, IReadOnlyDictionary<int, StayPricing>? preservedPrices = null)
        {
            var ids = inputs.Select(i => i.DestinationId).Distinct().ToList();
            var destinations = (await _unitOfWork.Repository<Destination>()
                .GetAllAsync(new BaseSpecifications<Destination>(d => ids.Contains(d.Id)))).ToDictionary(d => d.Id);

            return inputs.Select((input, index) =>
            {
                var stay = _mapper.Map<BookingStay>(input);
                stay.SortOrder = index;
                stay.IsCruise = destinations.TryGetValue(input.DestinationId, out var d) && d.IsCruise;

                if (preservedPrices is null)
                {
                    stay.ExtraNightsArrangedByGuest = input.ExtraNightsArrangedByGuest;
                    stay.Rates = (input.Rates ?? new())
                        // A row at zero rooms isn't priced, and a blank box is "not entered", not free.
                        .Where(r => r.Quantity > 0 && r.Rate.HasValue)
                        .Select(r => new BookingStayRate
                        {
                            RoomType = r.RoomType,
                            Quantity = r.Quantity,
                            Rate = r.Rate!.Value,
                        })
                        .ToList();
                }
                else if (preservedPrices.TryGetValue(index, out var kept))
                {
                    stay.ExtraNightsArrangedByGuest = kept.ArrangedByGuest;
                    // Fresh instances: the stored rows are being deleted alongside their old stay.
                    stay.Rates = kept.Rates
                        .Select(r => new BookingStayRate { RoomType = r.RoomType, Quantity = r.Quantity, Rate = r.Rate })
                        .ToList();
                }

                // Nothing can sit behind a row that stopped being extra — the rate panel is hidden in
                // that state, so a stale figure or flag would resurface unseen the moment someone
                // raised the nights again.
                if (stay.ExtraNights == 0)
                {
                    stay.Rates.Clear();
                    stay.ExtraNightsArrangedByGuest = false;
                }

                // Rates are kept when the guest arranged the nights: the checkbox sits directly beside
                // them in an open panel, so nothing is hidden, and unticking a mis-click must not cost
                // the operator the figures they just typed. ExtraNightsTotal returns 0 regardless.
                return stay;
            }).ToList();
        }

        // SortOrder is re-derived from position; the lead flag comes from the form's single radio group.
        private static List<BookingGuest> BuildGuests(BookingViewModel model) =>
            (model.Guests ?? new()).Select((input, index) => new BookingGuest
            {
                FullName = input.FullName.Trim(),
                Type = input.Type,
                Age = input.Type == GuestType.Adult ? null : input.Age,
                IsLeadGuest = index == model.LeadGuestIndex,
                SortOrder = index,
            }).ToList();

        private static List<BookingRoom> BuildRooms(BookingViewModel model) =>
            (model.Rooms ?? new()).Select((input, index) => new BookingRoom
            {
                Type = input.Type,
                Quantity = input.Quantity,
                SortOrder = index,
            }).ToList();

        /// <summary>
        /// Rebuilt wholesale from the posted rows, like Rooms — they're pure form state with no files
        /// to preserve.
        ///
        /// <para>
        /// 🔴 <paramref name="preserved"/> carries the WHOLE stored rows forward for a user without
        /// EditPricing, not just their prices. The tours card (<c>_BookingExtraTours</c>) renders
        /// nothing at all for that user, so their post contains no <c>Tours[...]</c> fields — and
        /// rebuilding from that empty list silently deleted every extra tour on the booking. It
        /// previously preserved only <c>UnitPrice</c>, keyed by index, which protects the price of
        /// rows that were never posted in the first place.
        /// </para>
        /// </summary>
        private static List<BookingTour> BuildTours(
            BookingViewModel model, IReadOnlyList<BookingTour>? preserved)
        {
            if (preserved is not null)
                return preserved.Select((t, index) => new BookingTour
                {
                    TourId = t.TourId,
                    Pax = t.Pax,
                    UnitPrice = t.UnitPrice,
                    Notes = t.Notes,
                    SortOrder = index,
                }).ToList();

            return (model.Tours ?? new()).Select((input, index) => new BookingTour
            {
                TourId = input.TourId,
                Pax = input.Pax,
                UnitPrice = input.UnitPrice,
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                SortOrder = index,
            }).ToList();
        }

        private async Task<List<BookingAttachment>> BuildNewAttachmentsAsync(BookingViewModel model)
        {
            var attachments = new List<BookingAttachment>();
            foreach (var (files, category) in new (List<IFormFile> Files, AttachmentCategory Category)[]
            {
                (model.FlightFiles, AttachmentCategory.Flight),
                (model.PassportFiles, AttachmentCategory.Passport),
            })
            {
                foreach (var file in files.Where(f => f.Length > 0))
                {
                    await using var stream = file.OpenReadStream();
                    var fileName = await _fileStorage.SaveFileAsync(stream, file.FileName, AttachmentsFolder);
                    attachments.Add(new BookingAttachment
                    {
                        Category = category,
                        FileUrl = $"/Files/{AttachmentsFolder}/{fileName}",
                        OriginalFileName = file.FileName,
                    });
                }
            }
            return attachments;
        }

        private static List<BookingAttachmentViewModel> ToAttachmentViewModels(IEnumerable<BookingAttachment> attachments) =>
            attachments
                .Where(a => a.Category != AttachmentCategory.Contract)
                .Select(a => new BookingAttachmentViewModel
                {
                    Id = a.Id,
                    Category = a.Category,
                    FileUrl = a.FileUrl,
                    OriginalFileName = a.OriginalFileName,
                    LinkUrl = a.LinkUrl,
                })
                .ToList();

        private static void ApplyExtraNights(Booking booking, Package package, int scheduledNights)
        {
            if (!package.TotalNights.HasValue)
            {
                booking.ExtraNights = 0;
                booking.ExtensionReason = null;
                return;
            }

            booking.ExtraNights = Math.Max(0, scheduledNights - package.TotalNights.Value);
            if (booking.ExtraNights == 0)
                booking.ExtensionReason = null;
            else if (string.IsNullOrEmpty(booking.ExtensionReason))
                booking.ExtensionReason = $"This booking runs {booking.ExtraNights} night(s) beyond the package total of {package.TotalNights.Value}.";
        }

        // isCreate=false skips the past-arrival-date check — Edit legitimately touches bookings whose
        // trip has already happened (e.g. attaching a passport scan or contract after the fact).
        private void ValidateBooking(BookingViewModel model, Package? package, bool isCreate)
        {
            if (isCreate && model.ArrivalDate < DateOnly.FromDateTime(DateTime.Today))
                ModelState.AddModelError(nameof(model.ArrivalDate), "Arrival date can't be in the past.");

            if (package is null)
            {
                ModelState.AddModelError(nameof(model.PackageId), "Selected package doesn't exist.");
                return;
            }

            if (!package.IsActive)
                ModelState.AddModelError(nameof(model.PackageId), "Selected package is inactive.");

            if (package.Segments.Any(s => s.Destination.IsCruise && s.Destination.DepartureDays is null))
                ModelState.AddModelError(nameof(model.PackageId),
                    "This package's cruise has no departure days set. Configure them on the Destination first.");

            if (!package.TotalNights.HasValue && model.DepartureDate <= model.ArrivalDate)
                ModelState.AddModelError(nameof(model.DepartureDate), "Departure must be after arrival (at least 1 night).");
        }

        /// <summary>
        /// Every pax type with a count must carry a sale price. Runs on Create and Edit alike — unlike
        /// the past-arrival-date rule above, pricing a booking is not a create-only concern.
        ///
        /// <para>
        /// Only called when the user holds <see cref="Permissions.Bookings.EditPricing"/>: someone who
        /// can edit a booking but not price it must still be able to save an unpriced one, otherwise
        /// every booking taken before pricing existed becomes uneditable for them.
        /// </para>
        /// </summary>
        private void ValidatePricing(BookingViewModel model)
        {
            foreach (var (count, price, field, label) in new[]
            {
                (model.Adults, model.AdultPrice, nameof(model.AdultPrice), "Adult"),
                (model.Children, model.ChildPrice, nameof(model.ChildPrice), "Child"),
                (model.Infants, model.InfantPrice, nameof(model.InfantPrice), "Infant"),
            })
            {
                if (count > 0 && price is null)
                    ModelState.AddModelError(field, $"{label} price is required.");
            }

            // A stay the operator extended beyond the generated schedule is chargeable, so it needs
            // room rates. Rows that aren't extra are skipped, and so are nights the guest arranged
            // themselves — there is nothing to charge for those.
            var stays = model.Stays ?? new();
            for (var i = 0; i < stays.Count; i++)
            {
                var stay = stays[i];
                if (stay.ExtraNights == 0 || stay.ExtraNightsArrangedByGuest)
                    continue;

                var rates = stay.Rates ?? new();
                var priced = false;

                for (var j = 0; j < rates.Count; j++)
                {
                    if (rates[j].Quantity <= 0)
                        continue;

                    if (rates[j].Rate is null)
                        // Named, because the rate boxes live in a modal while these messages render
                        // in the row: stacked there, three identical "Enter a rate." lines couldn't
                        // be told apart. _BookingStayRows renders a span for exactly this key.
                        ModelState.AddModelError($"Stays[{i}].Rates[{j}].Rate",
                            $"{ExcelFormat.RoomTypeText(rates[j].RoomType)}: enter a rate.");
                    else
                        priced = true;
                }

                if (!priced && !rates.Any(r => r.Quantity > 0))
                    ModelState.AddModelError($"Stays[{i}].Rates",
                        "Enter the rooms and rate for the extra nights on this stay, or mark them as arranged by the guest.");
            }
        }

        /// <summary>Deposit floor: a tenth of what the booking is worth.</summary>
        private const decimal FirstPaymentFraction = 0.10m;

        /// <summary>
        /// Create only. A booking should not be filed with nothing paid on it.
        ///
        /// <para>
        /// 🔴 Two permissions and one nullable total gate this, and getting any of them backwards makes
        /// bookings unsaveable for somebody. It runs only when the user can record payments AND the
        /// booking has a total to take 10% of — a user who can't price produces no total, so there is
        /// no minimum to enforce and the rule is skipped rather than failed. The caller checks
        /// Transactions.Create; this method checks the total.
        /// </para>
        /// </summary>
        private void ValidateFirstPayment(BookingViewModel model, decimal? grandTotal)
        {
            if (grandTotal is not { } total || total <= 0m)
                return;

            var minimum = Math.Round(total * FirstPaymentFraction, 2, MidpointRounding.AwayFromZero);

            if (model.FirstPaymentAmount is not { } paid || paid <= 0m)
            {
                ModelState.AddModelError(nameof(model.FirstPaymentAmount),
                    $"A first payment is required — at least {Money.Format(minimum)} (10% of the total).");
                return;
            }

            if (paid < minimum)
            {
                ModelState.AddModelError(nameof(model.FirstPaymentAmount),
                    $"First payment must be at least {Money.Format(minimum)} (10% of the total).");
            }
        }

        /// <summary>
        /// The total as the posted form describes it. The entity's own GrandTotal can't be used here:
        /// validation runs before the booking graph is built, and on Edit the stored one is stale.
        /// </summary>
        private static decimal? PostedGrandTotal(BookingViewModel model)
        {
            var priced = (model.Adults == 0 || model.AdultPrice.HasValue)
                      && (model.Children == 0 || model.ChildPrice.HasValue)
                      && (model.Infants == 0 || model.InfantPrice.HasValue);

            if (!priced)
                return null;

            var pax = model.Adults * (model.AdultPrice ?? 0m)
                    + model.Children * (model.ChildPrice ?? 0m)
                    + model.Infants * (model.InfantPrice ?? 0m);

            var nights = (model.Stays ?? new()).Sum(s => s.ExtraNightsTotal);
            var tours = (model.Tours ?? new()).Sum(t => t.Pax * t.UnitPrice);

            return pax + nights + tours;
        }

        /// <summary>
        /// Copies the posted prices onto the entity. Kept out of the AutoMapper profile (which ignores
        /// all three) so it can be skipped wholesale for a user without EditPricing — their POST then
        /// leaves the stored figures untouched instead of nulling them.
        /// </summary>
        private static void ApplyPricing(Booking booking, BookingViewModel model)
        {
            // A price behind a zero count is dropped, so a leftover figure can't reappear if the
            // count is raised again later without anyone re-entering it.
            booking.AdultPrice = model.Adults > 0 ? model.AdultPrice : null;
            booking.ChildPrice = model.Children > 0 ? model.ChildPrice : null;
            booking.InfantPrice = model.Infants > 0 ? model.InfantPrice : null;
        }

        /// <summary>
        /// Keeps the roster consistent with the pax breakdown and enforces the age bands.
        /// Rooming is deliberately NOT checked against pax — it's operator-defined (children may share
        /// beds, extra beds vary by supplier), so quantities are accepted as entered.
        /// </summary>
        private void ValidateGuestsAndRooms(BookingViewModel model)
        {
            var guests = model.Guests ?? new();
            var total = model.Adults + model.Children + model.Infants;

            if (total is < 1 or > 50)
                ModelState.AddModelError(nameof(model.Adults), "Total travellers must be between 1 and 50.");

            if (guests.Count != total)
            {
                ModelState.AddModelError(string.Empty,
                    $"Enter a name for every traveller — {total} expected, {guests.Count} provided.");
            }
            else
            {
                foreach (var (type, expected, field) in new[]
                {
                    (GuestType.Adult, model.Adults, nameof(model.Adults)),
                    (GuestType.Child, model.Children, nameof(model.Children)),
                    (GuestType.Infant, model.Infants, nameof(model.Infants)),
                })
                {
                    var actual = guests.Count(g => g.Type == type);
                    if (actual != expected)
                        ModelState.AddModelError(field, $"{expected} {type.ToString().ToLower()}(s) expected but {actual} listed.");
                }
            }

            // Tours are optional, but a row that exists must name a tour and can't name it twice —
            // two rows for the same tour would double-charge with nothing on screen saying so.
            var tours = model.Tours ?? new();
            var seenTours = new HashSet<int>();
            for (var i = 0; i < tours.Count; i++)
            {
                if (tours[i].TourId <= 0)
                    ModelState.AddModelError($"Tours[{i}].TourId", "Select a tour or remove the row.");
                else if (!seenTours.Add(tours[i].TourId))
                    ModelState.AddModelError($"Tours[{i}].TourId", "This tour is already on the booking.");
            }

            for (var i = 0; i < guests.Count; i++)
            {
                var guest = guests[i];
                if (guest.Type == GuestType.Adult)
                    continue;

                var (min, max) = guest.Type == GuestType.Child ? (6, 12) : (0, 5);

                if (guest.Age is null)
                    ModelState.AddModelError($"Guests[{i}].Age", "Age is required.");
                else if (guest.Age < min || guest.Age > max)
                    ModelState.AddModelError($"Guests[{i}].Age", $"A {guest.Type.ToString().ToLower()} must be aged {min}-{max}.");
            }

            if (guests.Count > 0)
            {
                if (model.LeadGuestIndex < 0 || model.LeadGuestIndex >= guests.Count)
                    ModelState.AddModelError(nameof(model.LeadGuestIndex), "Select the lead guest.");
                else if (guests[model.LeadGuestIndex].Type != GuestType.Adult)
                    ModelState.AddModelError(nameof(model.LeadGuestIndex), "The lead guest must be an adult.");
            }

            var rooms = model.Rooms ?? new();
            if (rooms.Count == 0)
                ModelState.AddModelError(string.Empty, "Add at least one room.");
            else if (rooms.Select(r => r.Type).Distinct().Count() != rooms.Count)
                ModelState.AddModelError(string.Empty, "Each room type can only be listed once — use the quantity instead.");
        }

        private void ValidateAttachments(BookingViewModel model)
        {
            foreach (var (files, field) in new (List<IFormFile> Files, string Field)[]
            {
                (model.FlightFiles, nameof(model.FlightFiles)),
                (model.PassportFiles, nameof(model.PassportFiles)),
            })
            {
                ValidateAttachmentFiles(files, field);
            }
        }

        /// <param name="maxFiles">
        /// Null for the unlimited categories (flights, passports). The first payment's receipt passes
        /// <see cref="AttachmentRules.MaxReceiptFiles"/> — a payment carries at most one, and the
        /// dropzone's data-max-files is only a convenience.
        /// </param>
        private void ValidateAttachmentFiles(IEnumerable<IFormFile> files, string field, int? maxFiles = null)
        {
            var uploaded = files.Where(f => f.Length > 0).ToList();

            foreach (var file in uploaded)
            {
                if (AttachmentRules.Reject(file.FileName, file.Length) is { } reason)
                    ModelState.AddModelError(field, $"{file.FileName}: {reason}");
            }

            if (maxFiles is { } cap && uploaded.Count > cap)
                ModelState.AddModelError(field, AttachmentRules.TooManyReceiptsMessage);
        }

        // Server-side guard for the hard structural rules (client also enforces these).
        private void ValidateStaySchedule(BookingViewModel model)
        {
            var stays = (model.Stays ?? new()).OrderBy(s => s.SortOrder).ToList();
            if (stays.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "The schedule must have at least one stay.");
                return;
            }

            var cursor = model.ArrivalDate;
            foreach (var stay in stays)
            {
                // Without this a blank select posts DestinationId=0 and dies as an FK violation.
                if (stay.DestinationId <= 0)
                {
                    ModelState.AddModelError(string.Empty, "Every stay needs a destination.");
                    return;
                }
                if (stay.Nights < 1)
                {
                    ModelState.AddModelError(string.Empty, "Every stay must be at least 1 night.");
                    return;
                }
                if (stay.CheckInDate != cursor)
                {
                    ModelState.AddModelError(string.Empty, "The schedule has a gap or overlap — check-in dates must be contiguous from the arrival date.");
                    return;
                }
                cursor = cursor.AddDays(stay.Nights);
            }
        }

        /// <summary>
        /// Gives every stay exactly one rate row per <see cref="RoomType"/>, in enum order, merging
        /// whatever was posted or loaded over the blanks.
        ///
        /// <para>
        /// Two things depend on this and both fail badly without it. The view indexes
        /// <c>Stays[i].Rates[j]</c> positionally, so a short list throws rather than rendering a
        /// validation message; and MVC's collection binder stops at the first missing index, so a
        /// partial post silently yields an EMPTY rate list — the operator's figures gone with no error.
        /// It lives here because every render path goes through PopulateFormAsync.
        /// </para>
        /// </summary>
        private static void NormalizeStayRates(BookingViewModel model)
        {
            // The booking's own room tally, summed by type — the Rooms card allows two rows of the
            // same type, and reading the first would quietly under-count. booking-form.js does the
            // same client-side for rows that become extra after the page loads.
            var tally = (model.Rooms ?? new())
                .Where(r => r.Quantity > 0)
                .GroupBy(r => r.Type)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

            foreach (var stay in model.Stays ?? new())
            {
                var posted = (stay.Rates ?? new())
                    .GroupBy(r => r.RoomType)
                    .ToDictionary(g => g.Key, g => g.First());

                stay.Rates = Enum.GetValues<RoomType>()
                    .OrderBy(t => t)
                    .Select(type => posted.TryGetValue(type, out var row)
                        ? new BookingStayRateInput { RoomType = type, Quantity = row.Quantity, Rate = row.Rate }
                        : new BookingStayRateInput { RoomType = type })
                    .ToList();

                // Seed the quantities as a row first becomes chargeable, never overwriting figures
                // that are already there — "3 rooms on the trip, only 2 stay the extra night" is the
                // common case and must survive a re-render. Seeding here rather than only in JS is
                // what lets the form be priced at all with scripting unavailable.
                if (stay.ExtraNights > 0 && stay.Rates.All(r => r.Quantity == 0))
                {
                    foreach (var rate in stay.Rates)
                        rate.Quantity = tally.TryGetValue(rate.RoomType, out var rooms) ? rooms : 0;
                }
            }
        }

        private static Dictionary<int, string> BuildDestinationLookup(Package package)
        {
            var names = new Dictionary<int, string> { [package.BaseDestinationId] = package.BaseDestination.Name };
            foreach (var seg in package.Segments)
                names[seg.DestinationId] = seg.Destination.Name;
            return names;
        }

        // Every package, active or not — a booking on a retired package must still be filterable.
        private async Task<IEnumerable<SelectListItem>> BuildPackageFilterOptionsAsync()
        {
            var spec = new BaseSpecifications<Package>();
            spec.SetOrderBy(p => p.Name);
            var packages = await _unitOfWork.Repository<Package>().GetAllAsync(spec);
            return packages.Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
        }

        private async Task PopulateFormAsync(BookingViewModel model, Package? package)
        {
            // Set here rather than per-action because every render path — Create/Edit GET and both
            // POSTs' re-render — goes through this method, so the flags can't be missed on one of them.
            model.CanEditPricing = await CanAsync(Permissions.Bookings.EditPricing);
            model.CanViewPricing = model.CanEditPricing || await CanAsync(Permissions.Bookings.ViewPricing);
            model.CanCreateTransactions = await CanAsync(Permissions.Transactions.Create);

            NormalizeStayRates(model);

            var packageSpec = new BaseSpecifications<Package>(p => p.IsActive);
            packageSpec.SetOrderBy(p => p.Name);
            var packages = await _unitOfWork.Repository<Package>().GetAllAsync(packageSpec);
            model.PackageOptions = packages.Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();

            var destSpec = new BaseSpecifications<Destination>(d => d.IsActive);
            destSpec.SetOrderBy(d => d.Name);
            var destinations = await _unitOfWork.Repository<Destination>().GetAllAsync(destSpec);
            model.StayDestinationOptions = destinations
                .Select(d => new SelectListItem(d.IsCruise ? $"{d.Name} (cruise)" : d.Name, d.Id.ToString()))
                .ToList();

            var nationalitySpec = new BaseSpecifications<Nationality>();
            nationalitySpec.SetOrderBy(n => n.Name);
            var nationalities = await _unitOfWork.Repository<Nationality>().GetAllAsync(nationalitySpec);
            model.NationalityOptions = nationalities
                .Select(n => new SelectListItem(n.Name, n.Id.ToString()))
                .ToList();

            // Active only — a retired tour stays on the bookings that already sold it (the FK is
            // Restrict) but must not be pickable on a new one.
            var tourSpec = new BaseSpecifications<Tour>(t => t.IsActive);
            tourSpec.SetOrderBy(t => t.Name);
            var tours = await _unitOfWork.Repository<Tour>().GetAllAsync(tourSpec);
            model.TourOptions = tours
                .Select(t => new TourOptionViewModel { Value = t.Id.ToString(), Text = t.Name, Price = t.PricePerPerson })
                .ToList();

            if (package is not null)
            {
                model.PackageHasFixedTotal = package.TotalNights.HasValue;
                model.PackageHasCruise = package.Segments.Any(s => s.Destination.IsCruise);
                model.PackageTotalNights = package.TotalNights;
                model.PackageBaseDestinationId = package.BaseDestinationId;

                // The embark picker is server-rendered on BOTH forms: on Edit a preview-on-load would
                // regenerate the saved schedule, so the radios have to exist before any AJAX runs.
                try
                {
                    var embarkOptions = _generator.GetEmbarkOptions(package, model.ArrivalDate);
                    model.EmbarkOptions = EmbarkOptionViewModel.Build(embarkOptions, package.BaseDestination.Name);
                    // Mirrors renderEmbark()'s "keep the current choice if it's still valid, else the
                    // soonest sailing" — otherwise nothing renders checked and nothing posts.
                    if (embarkOptions.Count > 0 && !embarkOptions.Any(o => o.Day == model.EmbarkDay))
                        model.EmbarkDay = embarkOptions[0].Day;
                }
                catch (InvalidOperationException)
                {
                    // The cruise destination has no departure days configured; ValidateBooking reports it.
                    model.EmbarkOptions = new();
                }
            }
        }
    }
}
