using System.Globalization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.InternalMovements
{
    /// <summary>
    /// The one owner of the Internal Movements rules: whether a booking is in a destination on a day,
    /// which flight a movement meets, which hotel the driver goes to, and whether a recorded movement
    /// still fits its booking. Pure — it reads only what it is handed — so the board, the calendar, the
    /// report and the save validation cannot drift (the <see cref="BlockSnapshot"/> rule).
    ///
    /// <para>
    /// Every method expects the booking with <c>Stays</c> loaded (plus <c>Guests</c> for
    /// <c>LeadGuestName</c>), and <paramref name="groups"/> holding the arrival-group records of the
    /// booking's stays with their <c>Flights.Bookings</c>. A group missing from the dictionary simply
    /// has no domestic flight.
    /// </para>
    /// </summary>
    public static class MovementPresence
    {
        public static DateOnly CheckOut(BookingStay stay) => stay.CheckInDate.AddDays(stay.Nights);

        /// <summary>The day the guest actually lands: the flight date, or the package date while none is recorded.</summary>
        public static DateOnly LandingDate(Booking booking) => booking.FlightArrivalDate ?? booking.ArrivalDate;

        public static DateOnly TakeOffDate(Booking booking) => booking.FlightDepartureDate ?? booking.DepartureDate;

        /// <summary>The Elsewhere rule: the trip, by its coalesced flight dates, includes this day.</summary>
        public static bool TripOverlaps(Booking booking, DateOnly day) =>
            LandingDate(booking) <= day && day <= TakeOffDate(booking);

        /// <summary>§5.1 — is the booking in the destination on the day, and is it arriving or departing.</summary>
        public static Presence Evaluate(
            Booking booking, int destinationId, DateOnly day,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups)
        {
            var stays = Ordered(booking);
            var here = stays.Where(s => s.DestinationId == destinationId).ToList();
            if (here.Count == 0)
                return new Presence(false, false, false, Array.Empty<FlightHint>());

            var hints = new List<FlightHint>();

            var covering = here.Any(s => s.CheckInDate <= day && day <= CheckOut(s));
            var stayStarts = here.Any(s => s.CheckInDate == day);
            var stayEnds = here.Any(s => CheckOut(s) == day);

            var intlArrival = LandingDate(booking) == day && stays[0].DestinationId == destinationId;
            if (intlArrival)
                hints.Add(new FlightHint(FlightLeg.InternationalArrival, International(booking, FlightLeg.InternationalArrival)));

            var intlDeparture = TakeOffDate(booking) == day && stays[^1].DestinationId == destinationId;
            if (intlDeparture)
                hints.Add(new FlightHint(FlightLeg.InternationalDeparture, International(booking, FlightLeg.InternationalDeparture)));

            var domesticArrivals = ArrivalFlights(booking, stays, destinationId, groups)
                .Where(f => f.FlightDate == day).ToList();
            hints.AddRange(domesticArrivals.Select(f => new FlightHint(FlightLeg.DomesticArrival, ToResolved(f))));

            var domesticDepartures = DepartureFlights(booking, stays, destinationId, groups)
                .Where(f => f.FlightDate == day).ToList();
            hints.AddRange(domesticDepartures.Select(f => new FlightHint(FlightLeg.DomesticDeparture, ToResolved(f))));

            var qualifies = covering || intlArrival || intlDeparture
                            || domesticArrivals.Count > 0 || domesticDepartures.Count > 0;

            return new Presence(
                qualifies,
                Arriving: stayStarts || intlArrival || domesticArrivals.Count > 0,
                Departing: stayEnds || intlDeparture || domesticDepartures.Count > 0,
                hints);
        }

        /// <summary>
        /// §5.2 — the flight a movement of this leg meets, read live. Null only for a domestic leg no
        /// <c>BlockFlight</c> ticks the booking on; an international leg always resolves (its date is the
        /// package date while no flight date is entered), and <see cref="ResolvedFlight.IsRecorded"/>
        /// says whether a number or time is known.
        /// </summary>
        public static ResolvedFlight? ResolveFlight(
            FlightLeg leg, Booking booking, int destinationId, DateOnly day,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups)
        {
            var stays = Ordered(booking);
            var candidates = leg switch
            {
                FlightLeg.InternationalArrival or FlightLeg.InternationalDeparture =>
                    null,
                FlightLeg.DomesticArrival => ArrivalFlights(booking, stays, destinationId, groups),
                FlightLeg.DomesticDeparture => DepartureFlights(booking, stays, destinationId, groups),
                _ => null,
            };

            if (candidates is null)
                return International(booking, leg);

            // Several can match (two Cairo stays, a second flight for the remaining guests): the one
            // nearest the board day is the one this movement meets.
            var nearest = candidates
                .OrderBy(f => Math.Abs(f.FlightDate.DayNumber - day.DayNumber))
                .ThenBy(f => f.FlightDate)
                .ThenBy(f => f.FlightTime)
                .FirstOrDefault();

            return nearest is null ? null : ToResolved(nearest);
        }

        public static IReadOnlyDictionary<FlightLeg, ResolvedFlight?> ResolveAll(
            Booking booking, int destinationId, DateOnly day,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups) =>
            Enum.GetValues<FlightLeg>().ToDictionary(
                leg => leg,
                leg => ResolveFlight(leg, booking, destinationId, day, groups));

        /// <summary>
        /// §5.3 — the hotel at the destination on the day: the stay covering it, else the nearest next
        /// stay here (the night-before arrival), else the nearest previous one. Null when blank.
        /// </summary>
        public static string? Hotel(Booking booking, int destinationId, DateOnly day)
        {
            var here = Ordered(booking).Where(s => s.DestinationId == destinationId).ToList();

            // A check-out day belongs to the night before, so prefer a stay the guest sleeps in.
            var stay = here.FirstOrDefault(s => s.CheckInDate <= day && day < CheckOut(s))
                       ?? here.FirstOrDefault(s => s.CheckInDate <= day && day <= CheckOut(s))
                       ?? here.Where(s => s.CheckInDate > day).OrderBy(s => s.CheckInDate).FirstOrDefault()
                       ?? here.Where(s => CheckOut(s) < day).OrderByDescending(s => s.CheckInDate).FirstOrDefault();

            return string.IsNullOrWhiteSpace(stay?.HotelName) ? null : stay!.HotelName!.Trim();
        }

        /// <summary>Where an Elsewhere booking is on the day — the destination of the stay covering it.</summary>
        public static string? WhereOn(Booking booking, DateOnly day)
        {
            var stays = Ordered(booking);
            var stay = stays.FirstOrDefault(s => s.CheckInDate <= day && day < CheckOut(s))
                       ?? stays.FirstOrDefault(s => s.CheckInDate <= day && day <= CheckOut(s));
            return stay?.Destination?.Name?.Trim();
        }

        /// <summary>
        /// §6 — why a recorded movement no longer fits its booking, or null when it still does. Checked
        /// in the spec's order, so a cancelled booking reads "Booking cancelled" rather than "no longer in".
        /// </summary>
        public static string? ReviewReason(
            InternalMovement movement, Booking booking, string destinationName,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups)
        {
            if (booking.Status == BookingStatus.Cancelled)
                return "Booking cancelled";

            if (!Evaluate(booking, movement.DestinationId, movement.Date, groups).Qualifies)
                return $"Booking is no longer in {destinationName.Trim()} on this day";

            if (movement.MovementType?.FlightLeg is { } leg
                && ResolveFlight(leg, booking, movement.DestinationId, movement.Date, groups) is { } flight
                && flight.Date != movement.Date)
                return $"Flight is now on {flight.Date.ToString("dd MMM", CultureInfo.InvariantCulture)}";

            if (movement.Pax > booking.Pax)
                return $"Booking now has {booking.Pax} pax";

            return null;
        }

        /// <summary>Every arrival-group key a booking's stays belong to — what the caller must load into <c>groups</c>.</summary>
        public static IEnumerable<BlockKey> GroupKeys(Booking booking) =>
            booking.Stays.Select(s => BlockKey.Of(booking, s));

        private static List<BookingStay> Ordered(Booking booking) =>
            booking.Stays.OrderBy(s => s.SortOrder).ThenBy(s => s.CheckInDate).ToList();

        private static ResolvedFlight International(Booking booking, FlightLeg leg) =>
            leg == FlightLeg.InternationalArrival
                ? new ResolvedFlight(booking.ArrivalFlightNo, booking.ArrivalTime, LandingDate(booking))
                : new ResolvedFlight(booking.DepartureFlightNo, booking.DepartureTime, TakeOffDate(booking));

        private static ResolvedFlight ToResolved(BlockFlight flight) =>
            new(flight.FlightNumber, flight.FlightTime, flight.FlightDate);

        // Domestic flights into here: those ticking the booking on the group of one of its stays here.
        private static IEnumerable<BlockFlight> ArrivalFlights(
            Booking booking, List<BookingStay> stays, int destinationId,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups) =>
            stays.Where(s => s.DestinationId == destinationId)
                 .SelectMany(s => FlightsOn(booking, s, groups));

        // Domestic flights out of here: those ticking the booking on the group of the stay right after a stay here.
        private static IEnumerable<BlockFlight> DepartureFlights(
            Booking booking, List<BookingStay> stays, int destinationId,
            IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups) =>
            stays.Select((s, i) => (Stay: s, Next: i + 1 < stays.Count ? stays[i + 1] : null))
                 .Where(p => p.Stay.DestinationId == destinationId && p.Next is not null)
                 .SelectMany(p => FlightsOn(booking, p.Next!, groups));

        private static IEnumerable<BlockFlight> FlightsOn(
            Booking booking, BookingStay stay, IReadOnlyDictionary<BlockKey, BlockServiceStatus> groups) =>
            groups.TryGetValue(BlockKey.Of(booking, stay), out var group)
                ? group.Flights.Where(f => f.Bookings.Any(l => l.BookingId == booking.Id))
                : Enumerable.Empty<BlockFlight>();
    }
}
