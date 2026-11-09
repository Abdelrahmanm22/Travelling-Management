using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services.InternalMovements;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.InternalMovements;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Internal Movements: pick a destination, then a day on its calendar, then assign drivers on the
    /// day board. Everything about presence, flights, hotels and "needs review" comes from
    /// <see cref="IInternalMovementQuery"/>; this controller only validates and writes movements.
    /// </summary>
    [Authorize]
    public class InternalMovementController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IInternalMovementQuery _query;
        private readonly IAuthorizationService _authorization;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<InternalMovementController> _logger;

        public InternalMovementController(
            IUnitOfWork unitOfWork,
            IInternalMovementQuery query,
            IAuthorizationService authorization,
            UserManager<AppUser> userManager,
            ILogger<InternalMovementController> logger)
        {
            _unitOfWork = unitOfWork;
            _query = query;
            _authorization = authorization;
            _userManager = userManager;
            _logger = logger;
        }

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        [Authorize(Policy = Permissions.InternalMovements.View)]
        public async Task<IActionResult> Index()
        {
            var destinations = await LoadFlaggedDestinationsAsync();

            // One flagged destination is the common case: skip the picker.
            if (destinations.Count == 1)
                return RedirectToAction(nameof(Calendar), new { destinationId = destinations[0].Id });

            return View(new InternalMovementIndexViewModel { Destinations = destinations });
        }

        [Authorize(Policy = Permissions.InternalMovements.View)]
        public async Task<IActionResult> Calendar(int destinationId, string? month)
        {
            var destinations = await LoadFlaggedDestinationsAsync();
            var destination = destinations.FirstOrDefault(d => d.Id == destinationId);
            if (destination is null)
                return NotFound();

            var first = DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : new DateOnly(Today.Year, Today.Month, 1);

            var days = await _query.GetMonthAsync(destination.Id, destination.Name, first);

            return View(new InternalMovementCalendarViewModel
            {
                DestinationId = destination.Id,
                DestinationName = destination.Name.Trim(),
                Month = first,
                Today = Today,
                Days = days,
                OtherDestinations = destinations.Where(d => d.Id != destination.Id).ToList(),
            });
        }

        [Authorize(Policy = Permissions.InternalMovements.View)]
        public async Task<IActionResult> Day(int destinationId, DateOnly? date)
        {
            var destination = await LoadFlaggedDestinationAsync(destinationId);
            if (destination is null)
                return NotFound();

            var day = date ?? Today;
            var board = await _query.GetDayAsync(destination.Id, destination.Name, day);

            var typeSpec = new BaseSpecifications<MovementType>(t => t.IsActive);
            typeSpec.SetOrderBy(t => t.Name);
            var driverSpec = new BaseSpecifications<Driver>(d => d.IsActive);
            driverSpec.SetOrderBy(d => d.Name);

            return View(new InternalMovementDayViewModel
            {
                Board = board,
                Types = (await _unitOfWork.Repository<MovementType>().GetAllAsync(typeSpec)).ToList(),
                Drivers = (await _unitOfWork.Repository<Driver>().GetAllAsync(driverSpec)).ToList(),
                // Resolved once: without Manage the board still renders, read-only.
                CanManage = (await _authorization.AuthorizeAsync(User, Permissions.InternalMovements.Manage)).Succeeded,
                CanOpenBooking = (await _authorization.AuthorizeAsync(User, Permissions.Bookings.View)).Succeeded,
                Today = Today,
            });
        }

        /// <summary>
        /// The popup's save. JSON body, antiforgery token in the <c>RequestVerificationToken</c> header.
        /// Every refusal comes back as readable messages the modal shows while staying open.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.InternalMovements.Manage)]
        public async Task<IActionResult> Save([FromBody] MovementSaveRequest? request)
        {
            if (request is null)
                return Refuse("The form couldn't be read. Reload the page and try again.", "Movement save refused: unreadable body");

            return request.Id is { } id
                ? await EditAsync(id, request)
                : await CreateAsync(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.InternalMovements.Manage)]
        public async Task<IActionResult> Delete(int id)
        {
            var movement = await _unitOfWork.Repository<InternalMovement>()
                .GetByIdAsync(new BaseSpecifications<InternalMovement>(m => m.Id == id));
            if (movement is null)
            {
                _logger.LogWarning("Delete refused: movement #{MovementId} not found", id);
                return Json(new { success = false, message = "This movement no longer exists. Reload the board." });
            }

            _unitOfWork.Repository<InternalMovement>().Delete(movement);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Movement #{MovementId} deleted", id);

            return Json(new { success = true, message = "Movement deleted." });
        }

        private async Task<IActionResult> CreateAsync(MovementSaveRequest request)
        {
            var errors = new List<string>();

            var destination = await LoadFlaggedDestinationAsync(request.DestinationId);
            if (destination is null)
                return Refuse("This destination doesn't take internal movements.",
                    "Movement save refused: destination #{DestinationId} not flagged", request.DestinationId);

            var type = await LoadTypeAsync(request.MovementTypeId);
            if (type is null || !type.IsActive)
                errors.Add("Choose an active movement type.");

            var driver = await LoadDriverAsync(request.DriverId);
            if (driver is null || !driver.IsActive)
                errors.Add("Choose an active driver.");

            var pickup = ParseTime(request.PickupTime, errors);

            var inputs = request.Bookings ?? new List<MovementBookingInput>();
            if (inputs.Count == 0)
                errors.Add("Select at least one booking.");
            if (inputs.Select(b => b.BookingId).Distinct().Count() != inputs.Count)
                errors.Add("A booking is listed twice. Reload the board and try again.");

            var qualification = await _query.QualifyAsync(destination.Id, request.Date, inputs.Select(b => b.BookingId).ToList());
            CheckBookings(destination, qualification, inputs, errors);

            if (errors.Count > 0)
                return Refuse(errors, "Movement save refused for {Count} booking(s) in {Destination} on {Date}: {Errors}",
                    inputs.Count, destination.Name.Trim(), request.Date.ToString("yyyy-MM-dd"), string.Join("; ", errors));

            var userName = await CurrentUserNameAsync();
            var created = new List<InternalMovement>();
            foreach (var input in inputs)
            {
                var movement = new InternalMovement
                {
                    BookingId = input.BookingId,
                    DestinationId = destination.Id,
                    Date = request.Date,
                    MovementTypeId = type!.Id,
                    DriverId = driver!.Id,
                    PickupTime = pickup!.Value,
                    Pax = input.Pax,
                    CreatedByName = userName,
                };
                await _unitOfWork.Repository<InternalMovement>().AddAsync(movement);
                created.Add(movement);
            }

            // One SaveChanges: the whole selection lands, or none of it.
            await _unitOfWork.CompleteAsync();

            var codes = string.Join(", ", inputs.Select(i => qualification.Qualified[i.BookingId].Code));
            if (created.Count == 1)
                _logger.LogInformation("Movement #{MovementId} ({MovementType}) recorded for {BookingCode} in {Destination} on {Date}, driver #{DriverId}",
                    created[0].Id, type!.Name, codes, destination.Name.Trim(), request.Date.ToString("yyyy-MM-dd"), driver!.Id);
            else
                _logger.LogInformation("{Count} movements ({MovementType}) recorded for {BookingCodes} in {Destination} on {Date}, driver #{DriverId}",
                    created.Count, type!.Name, codes, destination.Name.Trim(), request.Date.ToString("yyyy-MM-dd"), driver!.Id);

            return Json(new
            {
                success = true,
                message = created.Count == 1 ? "Movement recorded." : $"{created.Count} movements recorded.",
            });
        }

        private async Task<IActionResult> EditAsync(int id, MovementSaveRequest request)
        {
            var movement = await _unitOfWork.Repository<InternalMovement>()
                .GetByIdAsync(new InternalMovementSpec(id));
            if (movement is null)
                return Refuse("This movement no longer exists. Reload the board.",
                    "Movement edit refused: #{MovementId} not found", id);

            var errors = new List<string>();

            // An unchanged type or driver that has since been retired may stay; switching to one may not.
            var type = await LoadTypeAsync(request.MovementTypeId);
            if (type is null || (!type.IsActive && type.Id != movement.MovementTypeId))
                errors.Add("Choose an active movement type.");

            var driver = await LoadDriverAsync(request.DriverId);
            if (driver is null || (!driver.IsActive && driver.Id != movement.DriverId))
                errors.Add("Choose an active driver.");

            var pickup = ParseTime(request.PickupTime, errors);

            // The booking, destination and day come from the stored row, never the post.
            var input = request.Bookings?.FirstOrDefault(b => b.BookingId == movement.BookingId)
                        ?? new MovementBookingInput { BookingId = movement.BookingId, Pax = 0 };

            var qualification = await _query.QualifyAsync(movement.DestinationId, movement.Date, new[] { movement.BookingId });
            CheckBookings(movement.Destination, qualification, new[] { input }, errors);

            if (errors.Count > 0)
                return Refuse(errors, "Movement #{MovementId} edit refused: {Errors}", id, string.Join("; ", errors));

            movement.MovementTypeId = type!.Id;
            movement.DriverId = driver!.Id;
            movement.PickupTime = pickup!.Value;
            movement.Pax = input.Pax;
            movement.UpdatedAt = DateTime.UtcNow;
            movement.UpdatedByName = await CurrentUserNameAsync();

            _unitOfWork.Repository<InternalMovement>().Update(movement);
            await _unitOfWork.CompleteAsync();
            _logger.LogInformation("Movement #{MovementId} edited for {BookingCode}, driver #{DriverId}",
                movement.Id, movement.Booking.Code, driver.Id);

            return Json(new { success = true, message = "Movement updated." });
        }

        // Every booking must be in the destination on the day right now, and no movement may carry more
        // pax than its booking — a stale page or a tampered post gets a readable message.
        private static void CheckBookings(
            Destination destination, QualificationResult qualification,
            IEnumerable<MovementBookingInput> inputs, List<string> errors)
        {
            if (qualification.MissingIds.Count > 0)
                errors.Add("A selected booking no longer exists. Reload the board.");

            foreach (var code in qualification.RefusedCodes)
                errors.Add($"{code} is no longer in {destination.Name.Trim()} on this day. Reload the board.");

            foreach (var input in inputs)
            {
                if (!qualification.Qualified.TryGetValue(input.BookingId, out var booking))
                    continue;

                if (input.Pax < 1)
                    errors.Add($"{booking.Code}: pax must be at least 1.");
                else if (input.Pax > booking.Pax)
                    errors.Add($"{booking.Code}: pax can't be more than the booking's {booking.Pax}.");
            }
        }

        private static TimeOnly? ParseTime(string? value, List<string> errors)
        {
            if (TimeOnly.TryParseExact(value?.Trim(), new[] { "HH:mm", "HH:mm:ss", "H:mm" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return time;

            errors.Add("Enter the pickup time.");
            return null;
        }

        private IActionResult Refuse(string message, string logTemplate, params object?[] args) =>
            Refuse(new List<string> { message }, logTemplate, args);

        private IActionResult Refuse(List<string> errors, string logTemplate, params object?[] args)
        {
            _logger.LogWarning(logTemplate, args);
            return Json(new { success = false, message = errors[0], errors });
        }

        private async Task<List<Destination>> LoadFlaggedDestinationsAsync()
        {
            var spec = new BaseSpecifications<Destination>(d => d.HasInternalMovements);
            spec.SetOrderBy(d => d.Name);
            return (await _unitOfWork.Repository<Destination>().GetAllAsync(spec)).ToList();
        }

        private Task<Destination?> LoadFlaggedDestinationAsync(int id) =>
            _unitOfWork.Repository<Destination>()
                .GetByIdAsync(new BaseSpecifications<Destination>(d => d.Id == id && d.HasInternalMovements))!;

        private Task<MovementType?> LoadTypeAsync(int id) =>
            _unitOfWork.Repository<MovementType>().GetByIdAsync(new BaseSpecifications<MovementType>(t => t.Id == id))!;

        private Task<Driver?> LoadDriverAsync(int id) =>
            _unitOfWork.Repository<Driver>().GetByIdAsync(new BaseSpecifications<Driver>(d => d.Id == id))!;

        // The full name, like BlockFlight.CreatedByName, so the two read the same on screen.
        private async Task<string?> CurrentUserNameAsync() =>
            (await _userManager.GetUserAsync(User))?.FullName;
    }
}
