using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>
    /// An arrival group's identity: destination + check-in date + the grade its bookings were sold at,
    /// plus — for a private booking only — that booking's id.
    /// A 4★ and a 5★ booking arriving at the same place on the same day go to different hotels, so they
    /// are two independent blocks — two statuses, two files, two sets of documents and expenses. A
    /// private booking (<see cref="Booking.IsPrivate"/>) is its own group at every stay; the shared
    /// block for the same destination, date and category is formed without it.
    /// </summary>
    public readonly record struct BlockKey(
        int DestinationId, DateOnly CheckInDate, PackageCategory Category, int? PrivateBookingId = null)
    {
        public bool IsPrivate => PrivateBookingId.HasValue;

        public static BlockKey Of(BookingStay stay) => Of(stay.Booking, stay);

        /// <summary>Reads the booking explicitly, so it can't depend on <c>stay.Booking</c> being fixed up.</summary>
        public static BlockKey Of(Booking booking, BookingStay stay) =>
            new(stay.DestinationId, stay.CheckInDate, booking.Category, booking.IsPrivate ? booking.Id : null);
    }

    /// <summary>How many rooms of one type a booking (or a whole block) needs.</summary>
    public sealed record RoomTally(RoomType Type, int Quantity);

    /// <summary>One ticket file of a domestic flight.</summary>
    public sealed record BlockFlightFileLine(int Id, string FileUrl, string OriginalFileName)
    {
        public bool IsPdf => FileUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One domestic flight into an arrival group. <see cref="BookingIds"/> is every booking ticked on
    /// it — including any that have since left the group, which a caller showing the group's current
    /// bookings must filter out.
    /// </summary>
    public sealed record BlockFlightLine(
        int Id,
        string FlightNumber,
        DateOnly FlightDate,
        TimeOnly FlightTime,
        IReadOnlyList<BlockFlightFileLine> Files,
        IReadOnlySet<int> BookingIds,
        DateTime CreatedAt,
        string? CreatedByName)
    {
        public bool Covers(int bookingId) => BookingIds.Contains(bookingId);
    }

    /// <summary>
    /// The persisted state of one arrival group, or <see cref="NotBooked"/> when no row exists for it
    /// yet. Both email links are permanent and independent — a confirmation never replaces a request.
    ///
    /// <para>
    /// <see cref="Status"/> as built by <see cref="From"/> is the <b>stored</b> status.
    /// <see cref="BlockStatus.NeedsAmendment"/> is derived, so a caller that knows the group's current
    /// bookings applies <c>BlockSnapshot.Resolve</c> and re-stamps it via <see cref="WithStatus"/> —
    /// which leaves <see cref="StoredStatus"/> alone.
    /// </para>
    /// </summary>
    public sealed record BlockStatusInfo(
        BlockStatus Status,
        string? RequestEmailLink,
        string? ConfirmationEmailLink,
        DateTime? UpdatedAt,
        string? UpdatedByName,
        IReadOnlyList<BlockFlightLine> Flights)
    {
        /// <summary>
        /// The status actually stored on the row. <see cref="Status"/> may read
        /// <see cref="BlockStatus.NeedsAmendment"/>, which is derived and never stored — this is what
        /// the operator last saved, and so both what the status modal must pre-select and what says
        /// <i>which</i> document went out of date: the request we sent, or the confirmation we got.
        /// </summary>
        public BlockStatus StoredStatus { get; init; } = BlockStatus.NotBooked;

        /// <summary>What a group with no persisted row reads as.</summary>
        public static BlockStatusInfo NotBooked { get; } =
            new(BlockStatus.NotBooked, null, null, null, null, Array.Empty<BlockFlightLine>());

        /// <summary>The one place a stored row becomes display data, so the read and write sides agree.</summary>
        public static BlockStatusInfo From(BlockServiceStatus? row) =>
            row is null
                ? NotBooked
                : new BlockStatusInfo(
                    row.Status,
                    row.RequestEmailLink,
                    row.ConfirmationEmailLink,
                    row.UpdatedAt,
                    row.UpdatedByName,
                    row.Flights
                        .OrderBy(f => f.FlightDate).ThenBy(f => f.FlightTime).ThenBy(f => f.Id)
                        .Select(f => new BlockFlightLine(
                            f.Id, f.FlightNumber, f.FlightDate, f.FlightTime,
                            f.Files.OrderBy(a => a.UploadedAt).ThenBy(a => a.Id)
                                .Select(a => new BlockFlightFileLine(a.Id, a.FileUrl, a.OriginalFileName))
                                .ToList(),
                            f.Bookings.Select(b => b.BookingId).ToHashSet(),
                            f.CreatedAt, f.CreatedByName))
                        .ToList())
                {
                    StoredStatus = row.Status,
                };

        public BlockStatusInfo WithStatus(BlockStatus status) => this with { Status = status };

        public bool HasLinks => !string.IsNullOrWhiteSpace(RequestEmailLink)
                             || !string.IsNullOrWhiteSpace(ConfirmationEmailLink);

        public bool HasFlights => Flights.Count > 0;

        /// <summary>Whether any domestic flight on this group ticks the booking.</summary>
        public bool CoversFlight(int bookingId) => Flights.Any(f => f.Covers(bookingId));

        /// <summary>The flights on this group that tick the booking.</summary>
        public IReadOnlyList<BlockFlightLine> FlightsFor(int bookingId) =>
            Flights.Where(f => f.Covers(bookingId)).ToList();
    }

    /// <summary>
    /// One arrival group a single booking belongs to, with that group's status. A booking visiting three
    /// destinations produces three of these — and is only Confirmed once all three still cover it.
    /// </summary>
    /// <param name="IsCovered">
    /// Whether the supplier's confirmation for this group still covers <i>this</i> booking. A group can
    /// read <see cref="BlockStatus.NeedsAmendment"/> because a different booking changed, while this one
    /// remains covered and stays Confirmed. Always false unless the group's
    /// <see cref="BlockStatusInfo.StoredStatus"/> is <see cref="BlockStatus.Confirmed"/> — an
    /// In-progress group has recorded a request, which covers nothing.
    /// </param>
    public sealed record BookingBlockLine(
        int DestinationId,
        string DestinationName,
        DateOnly CheckInDate,
        PackageCategory Category,
        int? PrivateBookingId,
        int Nights,
        BlockStatusInfo Status,
        bool IsCovered);

    /// <summary>
    /// One row of the Block Services list: every booking whose itinerary checks into
    /// <see cref="DestinationId"/> on <see cref="CheckInDate"/> at <see cref="Category"/>, rolled up.
    /// 4★ and 5★ bookings go to different hotels, so they never share a block.
    /// </summary>
    public sealed record BlockSummary(
        int DestinationId,
        string DestinationName,
        bool IsCruise,
        DateOnly CheckInDate,
        PackageCategory Category,
        // Set when this group is one private booking's own file; null for a shared block.
        int? PrivateBookingId,
        string? PrivateBookingCode,
        int BookingCount,
        int TotalPax,
        int Adults,
        int Children,
        int Infants,
        IReadOnlyList<RoomTally> Rooms,
        int MinNights,
        int MaxNights,
        BlockStatusInfo Status,
        // How many of this group's bookings have a stay here marked as needing a domestic flight that
        // no flight on this group ticks. Screen-only: an internal gap is ours, not the supplier's.
        int MissingDomesticFlightCount)
    {
        public BlockKey Key => new(DestinationId, CheckInDate, Category, PrivateBookingId);
        public bool IsPrivate => PrivateBookingId.HasValue;
    }

    public sealed record BlockGuestLine(string FullName, GuestType Type, int? Age, bool IsLeadGuest);

    /// <summary>One booking inside a block file, flattened for display and export.</summary>
    public sealed record BlockBookingLine(
        int BookingId,
        string Code,
        string LeadGuestName,
        int Adults,
        int Children,
        int Infants,
        int Pax,
        IReadOnlyList<RoomTally> Rooms,
        int Nights,
        DateOnly CheckOutDate,
        string? HotelName,
        string? StayNotes,
        PackageCategory Category,
        string PackageName,
        // Every flight field is optional on the booking. The flight days fall back to the
        // package Arrival/DepartureDate beside them when null.
        string? ArrivalFlightNo,
        TimeOnly? ArrivalTime,
        DateOnly ArrivalDate,
        DateOnly? FlightArrivalDate,
        string? DepartureFlightNo,
        TimeOnly? DepartureTime,
        DateOnly DepartureDate,
        DateOnly? FlightDepartureDate,
        IReadOnlyList<BlockGuestLine> Guests,
        // See BlockSummary.MissingDomesticFlightCount — same fact, per booking, and equally screen-only.
        bool MissingDomesticFlightDoc,
        // The domestic flights on this group that tick this booking.
        IReadOnlyList<BlockFlightLine> DomesticFlights);

    public sealed record BlockFile(BlockSummary Summary, IReadOnlyList<BlockBookingLine> Bookings);

    /// <summary>
    /// Every arrival group in a filter window, split at the block/single line. Both halves are
    /// derived from one pass over the same stays, so a caller that needs the list <i>and</i> the
    /// other side's count (the Block Services alert) pays for a single query.
    /// </summary>
    public sealed record ArrivalGroups(
        IReadOnlyList<BlockSummary> Blocks,
        IReadOnlyList<BlockSummary> Singles);
}
