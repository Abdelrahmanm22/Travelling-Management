using Travel.Domain.Enums;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>How many rooms of one type a booking (or a whole block) needs.</summary>
    public sealed record RoomTally(RoomType Type, int Quantity);

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
        int MaxNights);

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
        IReadOnlyList<BlockGuestLine> Guests);

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
