using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.InternalMovements;
using Travel.Domain.Specifications.InternalMovements;
using Travel.Services.BlockServices;

namespace Travel.Services.InternalMovements
{
    /// <summary>
    /// The <see cref="BlockServiceQueryService"/> pattern: narrow by spec, then match in memory, since
    /// presence reads stays, flights and domestic flights together and <c>Pax</c> / <c>LeadGuestName</c>
    /// are computed C#. Each call is bookings + movements + one arrival-group load.
    /// </summary>
    public class InternalMovementQueryService : IInternalMovementQuery
    {
        private readonly IUnitOfWork _unitOfWork;

        public InternalMovementQueryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<CalendarDay>> GetMonthAsync(int destinationId, string destinationName, DateOnly monthStart)
        {
            var first = new DateOnly(monthStart.Year, monthStart.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            var bookings = await LoadBookingsAsync(first, last);
            var movements = await LoadMovementsAsync(destinationId, first, last);
            var groups = await LoadGroupsAsync(bookings.Concat(movements.Select(m => m.Booking)));

            var byDay = movements.ToLookup(m => m.Date);
            var days = new List<CalendarDay>();
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var present = bookings.Count(b => MovementPresence.Evaluate(b, destinationId, day, groups).Qualifies);
                var recorded = byDay[day].ToList();
                var review = recorded.Count(m => MovementPresence.ReviewReason(m, m.Booking, destinationName, groups) is not null);
                days.Add(new CalendarDay(day, present, recorded.Count, review));
            }

            return days;
        }

        public async Task<DayBoard> GetDayAsync(int destinationId, string destinationName, DateOnly date)
        {
            var bookings = await LoadBookingsAsync(date, date);
            var movements = await LoadMovementsAsync(destinationId, date, date);
            var groups = await LoadGroupsAsync(bookings.Concat(movements.Select(m => m.Booking)));

            var lines = movements
                .Select(m => ToLine(m, groups))
                .OrderBy(l => l.PickupTime).ThenBy(l => l.BookingCode)
                .ToList();
            var linesByBooking = lines.ToLookup(l => l.BookingId);

            var arriving = new List<BoardBooking>();
            var departing = new List<BoardBooking>();
            var staying = new List<BoardBooking>();
            var elsewhere = new List<BoardBooking>();

            foreach (var booking in bookings.OrderBy(b => b.Code, StringComparer.Ordinal))
            {
                var presence = MovementPresence.Evaluate(booking, destinationId, date, groups);
                if (presence.Qualifies)
                {
                    // A booking arriving and departing the same day sits under Arriving with both hints.
                    var section = presence.Arriving ? BoardSection.Arriving
                                : presence.Departing ? BoardSection.Departing
                                : BoardSection.Staying;
                    var card = Card(booking, section, presence.Hints,
                        MovementPresence.Hotel(booking, destinationId, date),
                        MovementPresence.ResolveAll(booking, destinationId, date, groups),
                        linesByBooking[booking.Id].ToList(), elsewhereLabel: null);

                    (section switch
                    {
                        BoardSection.Arriving => arriving,
                        BoardSection.Departing => departing,
                        _ => staying,
                    }).Add(card);
                }
                else if (MovementPresence.TripOverlaps(booking, date))
                {
                    elsewhere.Add(Card(booking, BoardSection.Elsewhere, Array.Empty<FlightHint>(),
                        hotel: null, new Dictionary<FlightLeg, ResolvedFlight?>(),
                        linesByBooking[booking.Id].ToList(),
                        elsewhereLabel: MovementPresence.WhereOn(booking, date) ?? "In transit"));
                }
            }

            return new DayBoard
            {
                DestinationId = destinationId,
                DestinationName = destinationName.Trim(),
                Date = date,
                NeedsReview = lines.Where(l => l.NeedsReview).ToList(),
                Arriving = arriving.OrderBy(EarliestHint).ThenBy(c => c.Code, StringComparer.Ordinal).ToList(),
                Departing = departing.OrderBy(EarliestHint).ThenBy(c => c.Code, StringComparer.Ordinal).ToList(),
                Staying = staying,
                Elsewhere = elsewhere.OrderBy(c => c.ElsewhereLabel).ThenBy(c => c.Code, StringComparer.Ordinal).ToList(),
                MovementCount = lines.Count,
            };
        }

        public async Task<IReadOnlyList<MovementLine>> GetReportLinesAsync(DateOnly? from, DateOnly? to, int? destinationId, int? driverId)
        {
            var movements = (await _unitOfWork.Repository<InternalMovement>()
                .GetAllAsync(new InternalMovementSpec(from, to, destinationId, driverId))).ToList();
            var groups = await LoadGroupsAsync(movements.Select(m => m.Booking));

            return movements
                .Select(m => ToLine(m, groups))
                .OrderBy(l => l.Date).ThenBy(l => l.PickupTime).ThenBy(l => l.BookingCode, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<QualificationResult> QualifyAsync(int destinationId, DateOnly date, IReadOnlyCollection<int> bookingIds)
        {
            var ids = bookingIds.Distinct().ToList();
            var bookings = ids.Count == 0
                ? new List<Booking>()
                : (await _unitOfWork.Repository<Booking>().GetAllAsync(new MovementBookingsSpec(ids))).ToList();
            var groups = await LoadGroupsAsync(bookings);

            var qualified = new Dictionary<int, QualifiedBooking>();
            var refused = new List<string>();
            foreach (var booking in bookings)
            {
                if (booking.Status != BookingStatus.Cancelled
                    && MovementPresence.Evaluate(booking, destinationId, date, groups).Qualifies)
                    qualified[booking.Id] = new QualifiedBooking(booking.Id, booking.Code, booking.Pax);
                else
                    refused.Add(booking.Code);
            }

            var missing = ids.Where(id => bookings.All(b => b.Id != id)).ToList();
            return new QualificationResult(qualified, refused, missing);
        }

        private async Task<List<Booking>> LoadBookingsAsync(DateOnly from, DateOnly to) =>
            // Widened a day each side: a domestic flight the evening before a check-in is routine, and
            // presence is decided exactly in memory anyway.
            (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new MovementBookingsSpec(from.AddDays(-1), to.AddDays(1)))).ToList();

        private async Task<List<InternalMovement>> LoadMovementsAsync(int destinationId, DateOnly from, DateOnly to) =>
            (await _unitOfWork.Repository<InternalMovement>()
                .GetAllAsync(new InternalMovementSpec(destinationId, from, to))).ToList();

        // One query for every arrival group any of these bookings belongs to — the domestic flights.
        private async Task<IReadOnlyDictionary<BlockKey, BlockServiceStatus>> LoadGroupsAsync(IEnumerable<Booking> bookings) =>
            await new BlockGroupLoader(_unitOfWork).LoadStatusesAsync(
                bookings.Distinct().SelectMany(MovementPresence.GroupKeys));

        private static MovementLine ToLine(InternalMovement m, IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups)
        {
            var booking = m.Booking;
            var leg = m.MovementType.FlightLeg;
            return new MovementLine
            {
                Id = m.Id,
                Date = m.Date,
                PickupTime = m.PickupTime,
                DestinationId = m.DestinationId,
                DestinationName = m.Destination.Name.Trim(),
                BookingId = booking.Id,
                BookingCode = booking.Code,
                LeadGuestName = booking.LeadGuestName,
                BookingPax = booking.Pax,
                Category = booking.Category,
                Pax = m.Pax,
                MovementTypeId = m.MovementTypeId,
                MovementTypeName = m.MovementType.Name,
                FlightLeg = leg,
                DriverId = m.DriverId,
                DriverName = m.Driver.Name,
                Hotel = MovementPresence.Hotel(booking, m.DestinationId, m.Date),
                Flight = leg is { } l ? MovementPresence.ResolveFlight(l, booking, m.DestinationId, m.Date, groups) : null,
                ReviewReason = MovementPresence.ReviewReason(m, booking, m.Destination.Name, groups),
                CreatedByName = m.CreatedByName,
                UpdatedByName = m.UpdatedByName,
            };
        }

        private static BoardBooking Card(
            Booking booking, BoardSection section, IReadOnlyList<FlightHint> hints, string? hotel,
            IReadOnlyDictionary<FlightLeg, ResolvedFlight?> flights, IReadOnlyList<MovementLine> movements,
            string? elsewhereLabel) =>
            new()
            {
                BookingId = booking.Id,
                Code = booking.Code,
                LeadGuestName = booking.LeadGuestName,
                Pax = booking.Pax,
                Category = booking.Category,
                IsPrivate = booking.IsPrivate,
                Section = section,
                Hotel = hotel,
                Hints = hints,
                Flights = flights,
                Movements = movements,
                ElsewhereLabel = elsewhereLabel,
            };

        // Cards with a flight today first, by its time; untimed ones last.
        private static TimeOnly EarliestHint(BoardBooking card) =>
            card.Hints.Select(h => h.Flight.Time).Where(t => t.HasValue).Select(t => t!.Value)
                .DefaultIfEmpty(TimeOnly.MaxValue).Min();
    }
}
