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
        private static readonly string[] AllowedAttachmentExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };
        private const long MaxAttachmentBytes = 10 * 1024 * 1024;

        // Kestrel's own default (30,000,000 bytes) is smaller than a handful of real photos across three
        // categories — the request gets killed before the action (or ValidateAttachments) ever runs, with
        // no ModelState error to show. Sized well above MaxAttachmentBytes × a realistic file count.
        private const long MaxAttachmentsRequestBytes = 100 * 1024 * 1024;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IItineraryGeneratorService _generator;
        private readonly ICodeGeneratorService _codes;
        private readonly IFileStorageService _fileStorage;
        private readonly BookingExcelExporter _excel;
        private readonly IBlockStatusService _blockStatus;

        public BookingController(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IItineraryGeneratorService generator,
            ICodeGeneratorService codes,
            IFileStorageService fileStorage,
            BookingExcelExporter excel,
            IBlockStatusService blockStatus)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _generator = generator;
            _codes = codes;
            _fileStorage = fileStorage;
            _excel = excel;
            _blockStatus = blockStatus;
        }

        [Authorize(Policy = Permissions.Bookings.View)]
        public async Task<IActionResult> Index(DateOnly? arrivalFrom, DateOnly? arrivalTo, int? packageId, BookingStatus? status)
        {
            var bookings = await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new BookingWithDetailsSpec(arrivalFrom, arrivalTo, packageId, status));

            return View(new BookingListViewModel
            {
                ArrivalFrom = arrivalFrom,
                ArrivalTo = arrivalTo,
                PackageId = packageId,
                Status = status,
                PackageOptions = await BuildPackageFilterOptionsAsync(),
                Bookings = bookings.ToList(),
            });
        }

        [Authorize(Policy = Permissions.Bookings.View)]
        public async Task<IActionResult> Details(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            return View(new BookingDetailsViewModel
            {
                Booking = booking,
                // Why this booking is (or isn't) confirmed — one line per arrival group it belongs to.
                Blocks = await _blockStatus.GetForBookingAsync(booking.Id),
            });
        }

        [Authorize(Policy = Permissions.Bookings.Export)]
        public async Task<IActionResult> Export(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            var bytes = _excel.Build(booking);
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

            ValidateBooking(model, package, isCreate: true);
            if (hasPostedStays)
                ValidateStaySchedule(model);
            ValidateGuestsAndRooms(model);
            ValidateAttachments(model);

            if (!ModelState.IsValid)
            {
                await PopulateFormAsync(model, package);
                return View(model);
            }

            var booking = _mapper.Map<Booking>(model);
            booking.CreatedAt = DateTime.UtcNow;

            if (hasPostedStays)
            {
                // The operator's table is the source of truth — same handling as Edit.
                booking.Stays = await BuildStaysFromInputsAsync(model.Stays);
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

            // Its stays decide which blocks it belongs to, so its status follows from them — a booking
            // created into an already-confirmed set of blocks lands Confirmed.
            await _blockStatus.RecalculateAsync(new[] { booking.Id });
            await _unitOfWork.CompleteAsync();

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
            ValidateBooking(model, package, isCreate: false);
            ValidateStaySchedule(model);
            ValidateGuestsAndRooms(model);
            ValidateAttachments(model);

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

            _mapper.Map(model, booking); // scalars only (Stays/DepartureDate/Extra* ignored by the map)

            // Replace stays with the operator's edited schedule (source of truth).
            foreach (var old in booking.Stays.ToList())
                _unitOfWork.Repository<BookingStay>().Delete(old);
            booking.Stays = await BuildStaysFromInputsAsync(model.Stays);
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

        private async Task<List<BookingStay>> BuildStaysFromInputsAsync(List<BookingStayInput> inputs)
        {
            var ids = inputs.Select(i => i.DestinationId).Distinct().ToList();
            var destinations = (await _unitOfWork.Repository<Destination>()
                .GetAllAsync(new BaseSpecifications<Destination>(d => ids.Contains(d.Id)))).ToDictionary(d => d.Id);

            return inputs.Select((input, index) =>
            {
                var stay = _mapper.Map<BookingStay>(input);
                stay.SortOrder = index;
                stay.IsCruise = destinations.TryGetValue(input.DestinationId, out var d) && d.IsCruise;
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

        private async Task<List<BookingAttachment>> BuildNewAttachmentsAsync(BookingViewModel model)
        {
            var attachments = new List<BookingAttachment>();
            foreach (var (files, category) in new (List<IFormFile> Files, AttachmentCategory Category)[]
            {
                (model.FlightFiles, AttachmentCategory.Flight),
                (model.PassportFiles, AttachmentCategory.Passport),
                (model.DomesticFlightFiles, AttachmentCategory.DomesticFlight),
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
                (model.DomesticFlightFiles, nameof(model.DomesticFlightFiles)),
            })
            {
                foreach (var file in files.Where(f => f.Length > 0))
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!AllowedAttachmentExtensions.Contains(ext))
                        ModelState.AddModelError(field, $"{file.FileName}: only PDF, JPG and PNG files are allowed.");
                    else if (file.Length > MaxAttachmentBytes)
                        ModelState.AddModelError(field, $"{file.FileName}: files must be 10MB or smaller.");
                }
            }
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
