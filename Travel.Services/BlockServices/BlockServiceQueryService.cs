using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications.BlockServices;

namespace Travel.Services.BlockServices
{
    /// <inheritdoc cref="IBlockServiceQuery" />
    public class BlockServiceQueryService : IBlockServiceQuery
    {
        /// <summary>
        /// Where a group stops being a single arrival and becomes a block. This is a
        /// <i>presentation</i> split — which of the two lists the group appears in — not an
        /// existence threshold: a one-booking group is still a real file with a real supplier
        /// document behind it.
        /// </summary>
        private const int MinimumBookings = 2;

        private readonly IUnitOfWork _unitOfWork;

        public BlockServiceQueryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <inheritdoc />
        public async Task<ArrivalGroups> GetArrivalsAsync(DateOnly? from, DateOnly? to, int? destinationId)
        {
            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(from, to, destinationId));

            var groups = stays
                .GroupBy(s => new { s.DestinationId, s.CheckInDate })
                .Select(g => BuildSummary(DistinctByBooking(g)))
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                .OrderBy(summary => summary.CheckInDate)
                .ThenBy(summary => summary.DestinationName)
                .ToList();

            // Partitioned from one materialisation so the list and the other page's count — both
            // needed on every request to either page — cost a single query.
            return new ArrivalGroups(
                Blocks: groups.Where(g => g.BookingCount >= MinimumBookings).ToList(),
                Singles: groups.Where(g => g.BookingCount < MinimumBookings).ToList());
        }

        /// <inheritdoc />
        public async Task<BlockFile?> GetBlockAsync(int destinationId, DateOnly checkIn)
        {
            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(destinationId, checkIn));

            var block = DistinctByBooking(stays);

            var summary = BuildSummary(block);
            if (summary is null) return null;

            var bookings = block
                .Select(BuildBookingLine)
                .OrderBy(line => line.Code, StringComparer.Ordinal)
                .ToList();

            return new BlockFile(summary, bookings);
        }

        /// <summary>
        /// One stay per booking. Guards the edge case of a single booking visiting the same destination
        /// twice on the same check-in date, which would otherwise double-count its pax and rooms.
        /// </summary>
        private static List<BookingStay> DistinctByBooking(IEnumerable<BookingStay> stays) =>
            stays.GroupBy(s => s.BookingId)
                 .Select(g => g.First())
                 .ToList();

        /// <summary>
        /// Rolls an arrival group's stays up into a list row, or null when nothing matched the key.
        /// A single booking rolls up exactly like a block does — the block/single split happens in
        /// <see cref="GetArrivalsAsync"/>, deliberately not here, so <see cref="GetBlockAsync"/> can
        /// serve a one-booking file.
        /// </summary>
        private static BlockSummary? BuildSummary(IReadOnlyList<BookingStay> stays)
        {
            if (stays.Count == 0) return null;

            var first = stays[0];
            var bookings = stays.Select(s => s.Booking).ToList();

            return new BlockSummary(
                DestinationId: first.DestinationId,
                // Destination names are operator-entered free text; trim so the list, the file header
                // and the exported filename don't inherit stray whitespace.
                DestinationName: first.Destination?.Name?.Trim() is { Length: > 0 } name ? name : "—",
                IsCruise: first.IsCruise,
                CheckInDate: first.CheckInDate,
                BookingCount: stays.Count,
                TotalPax: bookings.Sum(b => b.Pax),
                Adults: bookings.Sum(b => b.Adults),
                Children: bookings.Sum(b => b.Children),
                Infants: bookings.Sum(b => b.Infants),
                Rooms: TallyRooms(bookings.SelectMany(b => b.Rooms)),
                MinNights: stays.Min(s => s.Nights),
                MaxNights: stays.Max(s => s.Nights));
        }

        private static BlockBookingLine BuildBookingLine(BookingStay stay)
        {
            var booking = stay.Booking;

            var guests = booking.Guests
                .OrderBy(g => g.SortOrder)
                .Select(g => new BlockGuestLine(g.FullName, g.Type, g.Age, g.IsLeadGuest))
                .ToList();

            return new BlockBookingLine(
                BookingId: booking.Id,
                Code: booking.Code,
                LeadGuestName: booking.LeadGuestName,
                Adults: booking.Adults,
                Children: booking.Children,
                Infants: booking.Infants,
                Pax: booking.Pax,
                Rooms: TallyRooms(booking.Rooms),
                Nights: stay.Nights,
                CheckOutDate: stay.CheckInDate.AddDays(stay.Nights),
                HotelName: stay.HotelName,
                StayNotes: stay.Notes,
                Category: booking.Category,
                PackageName: booking.Package?.Name ?? "—",
                ArrivalFlightNo: booking.ArrivalFlightNo,
                ArrivalTime: booking.ArrivalTime,
                ArrivalDate: booking.ArrivalDate,
                FlightArrivalDate: booking.FlightArrivalDate,
                DepartureFlightNo: booking.DepartureFlightNo,
                DepartureTime: booking.DepartureTime,
                DepartureDate: booking.DepartureDate,
                FlightDepartureDate: booking.FlightDepartureDate,
                Guests: guests);
        }

        /// <summary>Sums quantities per room type, ordered SGL → DBL → TRP (the enum's own order).</summary>
        private static IReadOnlyList<RoomTally> TallyRooms(IEnumerable<BookingRoom> rooms) =>
            rooms.GroupBy(r => r.Type)
                 .Select(g => new RoomTally(g.Key, g.Sum(r => r.Quantity)))
                 .OrderBy(t => t.Type)
                 .ToList();
    }
}
