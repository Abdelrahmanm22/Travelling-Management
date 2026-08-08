using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications.BlockServices;
using Travel.Domain.Specifications.Bookings;

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
        public async Task<ArrivalGroups> GetArrivalsAsync(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status)
        {
            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(from, to, destinationId));

            // One query for every stored status in the same window, then a dictionary lookup per group.
            var statusRows = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetAllAsync(new BlockStatusSpec(from, to, destinationId));

            var statuses = statusRows.ToDictionary(r => (r.DestinationId, r.CheckInDate));

            var missingDocs = await LoadMissingDomesticDocsAsync(stays.Select(s => s.BookingId));

            var groups = stays
                .GroupBy(s => new { s.DestinationId, s.CheckInDate })
                .Select(g => BuildSummary(
                    DistinctByBooking(g),
                    Lookup(statuses, g.Key.DestinationId, g.Key.CheckInDate),
                    missingDocs))
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                // Filtered before the partition so both halves — and the cross-link count each page
                // shows for the other — agree on what the filter means.
                .Where(summary => status is null || summary.Status.Status == status)
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

            var statusRow = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetByIdAsync(new BlockStatusSpec(destinationId, checkIn));

            var missingDocs = await LoadMissingDomesticDocsAsync(block.Select(s => s.BookingId));

            var summary = BuildSummary(block, BlockStatusInfo.From(statusRow), missingDocs);
            if (summary is null) return null;

            var bookings = block
                .Select(stay => BuildBookingLine(stay, missingDocs))
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
        /// The ids of bookings that need a domestic flight document and don't have one, in one query.
        /// Loaded separately from the stays because the rule needs each booking's <i>whole</i> itinerary —
        /// see <see cref="BookingDocsSpec"/> for why the windowed <see cref="BlockStaysSpec"/> can't supply it.
        /// </summary>
        private async Task<HashSet<int>> LoadMissingDomesticDocsAsync(IEnumerable<int> bookingIds)
        {
            var ids = bookingIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();

            var bookings = await _unitOfWork.Repository<Booking>().GetAllAsync(new BookingDocsSpec(ids));

            return bookings.Where(b => b.MissingDomesticFlightDoc).Select(b => b.Id).ToHashSet();
        }

        /// <summary>
        /// Rolls an arrival group's stays up into a list row, or null when nothing matched the key.
        /// A single booking rolls up exactly like a block does — the block/single split happens in
        /// <see cref="GetArrivalsAsync"/>, deliberately not here, so <see cref="GetBlockAsync"/> can
        /// serve a one-booking file.
        /// </summary>
        private static BlockSummary? BuildSummary(
            IReadOnlyList<BookingStay> stays, BlockStatusInfo status, HashSet<int> missingDocs)
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
                MaxNights: stays.Max(s => s.Nights),
                Status: status,
                // stays is already DistinctByBooking'd, so this counts bookings, not stays.
                MissingDomesticFlightCount: stays.Count(s => missingDocs.Contains(s.BookingId)));
        }

        private static BlockStatusInfo Lookup(
            Dictionary<(int, DateOnly), BlockServiceStatus> statuses, int destinationId, DateOnly checkIn) =>
            statuses.TryGetValue((destinationId, checkIn), out var row)
                ? BlockStatusInfo.From(row)
                : BlockStatusInfo.NotBooked;

        private static BlockBookingLine BuildBookingLine(BookingStay stay, HashSet<int> missingDocs)
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
                Guests: guests,
                MissingDomesticFlightDoc: missingDocs.Contains(booking.Id));
        }

        /// <summary>Sums quantities per room type, ordered SGL → DBL → TRP (the enum's own order).</summary>
        private static IReadOnlyList<RoomTally> TallyRooms(IEnumerable<BookingRoom> rooms) =>
            rooms.GroupBy(r => r.Type)
                 .Select(g => new RoomTally(g.Key, g.Sum(r => r.Quantity)))
                 .OrderBy(t => t.Type)
                 .ToList();
    }
}
