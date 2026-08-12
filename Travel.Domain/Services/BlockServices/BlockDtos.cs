using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>How many rooms of one type a booking (or a whole block) needs.</summary>
    public sealed record RoomTally(RoomType Type, int Quantity);

    /// <summary>One domestic flight document held by an arrival group.</summary>
    public sealed record BlockDocumentLine(
        int Id,
        string FileUrl,
        string OriginalFileName,
        DateTime UploadedAt,
        string? UploadedByName)
    {
        public bool IsPdf => FileUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The persisted state of one arrival group, or <see cref="NotBooked"/> when no row exists for it
    /// yet. Both email links are permanent and independent — a confirmation never replaces a request.
    ///
    /// <para>
    /// <see cref="Status"/> as built by <see cref="From"/> is the <b>stored</b> status.
    /// <see cref="BlockStatus.NeedsAmendment"/> is derived, so a caller that knows the group's current
    /// bookings applies <c>BlockConfirmation.Resolve</c> and re-stamps it via <see cref="WithStatus"/>.
    /// </para>
    /// </summary>
    public sealed record BlockStatusInfo(
        BlockStatus Status,
        string? RequestEmailLink,
        string? ConfirmationEmailLink,
        DateTime? UpdatedAt,
        string? UpdatedByName,
        IReadOnlyList<BlockDocumentLine> Documents)
    {
        /// <summary>What a group with no persisted row reads as.</summary>
        public static BlockStatusInfo NotBooked { get; } =
            new(BlockStatus.NotBooked, null, null, null, null, Array.Empty<BlockDocumentLine>());

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
                    row.Attachments
                        .OrderBy(a => a.UploadedAt)
                        .Select(a => new BlockDocumentLine(
                            a.Id, a.FileUrl, a.OriginalFileName, a.UploadedAt, a.UploadedByName))
                        .ToList());

        public BlockStatusInfo WithStatus(BlockStatus status) => this with { Status = status };

        public bool HasLinks => !string.IsNullOrWhiteSpace(RequestEmailLink)
                             || !string.IsNullOrWhiteSpace(ConfirmationEmailLink);

        public bool HasDocuments => Documents.Count > 0;
    }

    /// <summary>
    /// One arrival group a single booking belongs to, with that group's status. A booking visiting three
    /// destinations produces three of these — and is only Confirmed once all three still cover it.
    /// </summary>
    /// <param name="IsCovered">
    /// Whether the supplier's confirmation for this group still covers <i>this</i> booking. A group can
    /// read <see cref="BlockStatus.NeedsAmendment"/> because a different booking changed, while this one
    /// remains covered and stays Confirmed.
    /// </param>
    public sealed record BookingBlockLine(
        int DestinationId,
        string DestinationName,
        DateOnly CheckInDate,
        int Nights,
        BlockStatusInfo Status,
        bool IsCovered);

    /// <summary>
    /// One row of the Block Services list: every booking whose itinerary checks into
    /// <see cref="DestinationId"/> on <see cref="CheckInDate"/>, rolled up.
    /// </summary>
    public sealed record BlockSummary(
        int DestinationId,
        string DestinationName,
        bool IsCruise,
        DateOnly CheckInDate,
        int BookingCount,
        int TotalPax,
        int Adults,
        int Children,
        int Infants,
        IReadOnlyList<RoomTally> Rooms,
        int MinNights,
        int MaxNights,
        BlockStatusInfo Status,
        // How many of this group's bookings need a domestic flight document and don't have one.
        // Screen-only: an internal document gap is ours, not the supplier's, so it stays out of the export.
        int MissingDomesticFlightCount);

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
        bool MissingDomesticFlightDoc);

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
