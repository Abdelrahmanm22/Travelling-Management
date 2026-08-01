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
        string ArrivalFlightNo,
        TimeOnly ArrivalTime,
        DateOnly ArrivalDate,
        string DepartureFlightNo,
        TimeOnly DepartureTime,
        DateOnly DepartureDate,
        IReadOnlyList<BlockGuestLine> Guests);

    public sealed record BlockFile(BlockSummary Summary, IReadOnlyList<BlockBookingLine> Bookings);
}
