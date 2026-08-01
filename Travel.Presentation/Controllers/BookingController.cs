using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.Itinerary;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Bookings;
using Travel.Domain.Specifications.Packages;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IItineraryGeneratorService _generator;

        public BookingController(IUnitOfWork unitOfWork, IMapper mapper, IItineraryGeneratorService generator)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _generator = generator;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = await _unitOfWork.Repository<Booking>().GetAllAsync(new BookingWithDetailsSpec());
            return View(bookings);
        }

        public async Task<IActionResult> Details(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            return View(booking);
        }

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
        public async Task<IActionResult> Create(BookingViewModel model)
        {
            var package = await LoadPackageAsync(model.PackageId);
            ValidateBooking(model, package);
            ValidateGuestsAndRooms(model);

            if (!ModelState.IsValid)
            {
                await PopulateFormAsync(model, package);
                return View(model);
            }

            var booking = _mapper.Map<Booking>(model);
            booking.CreatedAt = DateTime.UtcNow;
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
            booking.Guests = BuildGuests(model);
            booking.Rooms = BuildRooms(model);

            await _unitOfWork.Repository<Booking>().AddAsync(booking);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Booking created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            var model = _mapper.Map<BookingViewModel>(booking);
            var package = await LoadPackageAsync(booking.PackageId);
            await PopulateFormAsync(model, package);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(BookingViewModel model)
        {
            var package = await LoadPackageAsync(model.PackageId);
            ValidateBooking(model, package);
            ValidateStaySchedule(model);
            ValidateGuestsAndRooms(model);

            if (!ModelState.IsValid)
            {
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

            _unitOfWork.Repository<Booking>().Update(booking);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Booking updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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
            booking.DepartureDate = itinerary.DepartureDate;
            booking.ExtraNights = itinerary.ExtraNights;
            booking.ExtensionReason = itinerary.ExtensionReason;
            booking.IsManuallyEdited = false;

            _unitOfWork.Repository<Booking>().Update(booking);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Schedule regenerated from the package.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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
            var baseName = package.BaseDestination.Name;
            return Json(new
            {
                success = true,
                hasFixedTotal = hasFixed,
                hasCruise,
                departureDate = itinerary.DepartureDate.ToString("yyyy-MM-dd"),
                extraNights = itinerary.ExtraNights,
                extensionReason = itinerary.ExtensionReason,
                embarkOptions = embarkOptions.Select(o => new
                {
                    value = o.Day.ToString(),
                    text = $"{o.Date:ddd dd MMM} — {o.LeadNights} night{(o.LeadNights == 1 ? "" : "s")} in {baseName} first",
                    extraNights = o.ExtraNights,
                }).ToList<object>(),
                defaultEmbark = embarkOptions.Count > 0 ? embarkOptions[0].Day.ToString() : null,
                stays,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return Json(new { success = false, message = "Booking not found." });

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

        private void ValidateBooking(BookingViewModel model, Package? package)
        {
            if (model.ArrivalDate < DateOnly.FromDateTime(DateTime.Today))
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

            if (package is not null)
            {
                model.PackageHasFixedTotal = package.TotalNights.HasValue;
                model.PackageHasCruise = package.Segments.Any(s => s.Destination.IsCruise);
                model.PackageTotalNights = package.TotalNights;
                model.PackageBaseDestinationId = package.BaseDestinationId;
            }
        }
    }
}
