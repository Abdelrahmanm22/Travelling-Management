using Travel.Domain;
using Travel.Domain.Enums;
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
        public async Task<ArrivalGroups> GetArrivalsAsync(
            DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status, PackageCategory? category = null)
        {
            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(from, to, destinationId, category));

            // One query for every stored record in the same window, then a dictionary lookup per group.
            var statusRows = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetAllAsync(new BlockStatusSpec(from, to, destinationId));

            var statuses = statusRows.ToDictionary(BlockGroupLoader.KeyOf);

            var groups = stays
                .GroupBy(BlockKey.Of)
                .Select(g => BuildSummary(
                    DistinctByBooking(g),
                    statuses.GetValueOrDefault(g.Key),
                    BookingsNeedingFlight(g)))
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                // Filtered before the partition so both halves — and the cross-link count each page
                // shows for the other — agree on what the filter means.
                .Where(summary => status is null || summary.Status.Status == status)
                .OrderBy(summary => summary.CheckInDate)
                .ThenBy(summary => summary.DestinationName)
                .ThenBy(summary => summary.Category)
                // Shared block first, then each private file of the same date.
                .ThenBy(summary => summary.PrivateBookingId ?? 0)
                .ToList();

            // Partitioned from one materialisation so the list and the other page's count — both
            // needed on every request to either page — cost a single query.
            return new ArrivalGroups(
                Blocks: groups.Where(g => g.BookingCount >= MinimumBookings).ToList(),
                Singles: groups.Where(g => g.BookingCount < MinimumBookings).ToList());
        }

        /// <inheritdoc />
        public async Task<BlockFile?> GetBlockAsync(BlockKey key)
        {
            var stays = await _unitOfWork.Repository<BookingStay>().GetAllAsync(
                new BlockStaysSpec(key.DestinationId, key.CheckInDate, key.Category, key.PrivateBookingId));

            var block = DistinctByBooking(stays);

            var statusRow = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetByIdAsync(new BlockStatusSpec(key.DestinationId, key.CheckInDate, key.Category, key.PrivateBookingId));

            var needingFlight = BookingsNeedingFlight(stays);

            var summary = BuildSummary(block, statusRow, needingFlight);
            if (summary is null) return null;

            var bookings = block
                .Select(stay => BuildBookingLine(stay, summary.Status, needingFlight))
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
        /// The bookings whose stay in this group is marked as reached by a domestic flight. Read from
        /// every stay before <see cref="DistinctByBooking"/>, so a booking visiting the same destination
        /// twice on one check-in needs a flight when either stay does.
        /// </summary>
        private static HashSet<int> BookingsNeedingFlight(IEnumerable<BookingStay> stays) =>
            stays.Where(s => s.RequiresDomesticFlight).Select(s => s.BookingId).ToHashSet();

        /// <summary>
        /// Missing = this booking's stay here needs a domestic flight and no flight on <i>this</i> group
        /// ticks it. The rule is per group: the flight into Aswan belongs to the Aswan group, so a
        /// ticket recorded elsewhere on the itinerary says nothing about this leg.
        /// </summary>
        private static bool IsMissingFlight(int bookingId, BlockStatusInfo status, HashSet<int> needingFlight) =>
            needingFlight.Contains(bookingId) && !status.CoversFlight(bookingId);

        /// <summary>
        /// Rolls an arrival group's stays up into a list row, or null when nothing matched the key.
        /// A single booking rolls up exactly like a block does — the block/single split happens in
        /// <see cref="GetArrivalsAsync"/>, deliberately not here, so <see cref="GetBlockAsync"/> can
        /// serve a one-booking file.
        /// </summary>
        private static BlockSummary? BuildSummary(
            IReadOnlyList<BookingStay> stays, BlockServiceStatus? row, HashSet<int> needingFlight)
        {
            if (stays.Count == 0) return null;

            var first = stays[0];
            var bookings = stays.Select(s => s.Booking).ToList();

            // Confirmed only holds while the snapshot still describes the group; otherwise this
            // resolves to NeedsAmendment. Nothing about that is stored — see BlockSnapshot.
            var resolution = BlockSnapshot.Resolve(
                row?.Status ?? BlockStatus.NotBooked,
                stays,
                row?.RecordedBookings.ToList() ?? new List<BlockRecordedBooking>());

            var status = BlockStatusInfo.From(row).WithStatus(resolution.Status);

            return new BlockSummary(
                DestinationId: first.DestinationId,
                // Destination names are operator-entered free text; trim so the list, the file header
                // and the exported filename don't inherit stray whitespace.
                DestinationName: first.Destination?.Name?.Trim() is { Length: > 0 } name ? name : "—",
                IsCruise: first.IsCruise,
                CheckInDate: first.CheckInDate,
                Category: first.Booking.Category,
                PrivateBookingId: first.Booking.IsPrivate ? first.BookingId : null,
                PrivateBookingCode: first.Booking.IsPrivate ? first.Booking.Code : null,
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
                MissingDomesticFlightCount: stays.Count(s => IsMissingFlight(s.BookingId, status, needingFlight)));
        }

        private static BlockBookingLine BuildBookingLine(
            BookingStay stay, BlockStatusInfo status, HashSet<int> needingFlight)
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
                MissingDomesticFlightDoc: IsMissingFlight(booking.Id, status, needingFlight),
                DomesticFlights: status.FlightsFor(booking.Id));
        }

        /// <summary>Sums quantities per room type, ordered SGL → DBL → TRP (the enum's own order).</summary>
        private static IReadOnlyList<RoomTally> TallyRooms(IEnumerable<BookingRoom> rooms) =>
            rooms.GroupBy(r => r.Type)
                 .Select(g => new RoomTally(g.Key, g.Sum(r => r.Quantity)))
                 .OrderBy(t => t.Type)
                 .ToList();
    }
}
