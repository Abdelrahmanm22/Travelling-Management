using Travel.Domain.Models;

namespace Travel.Domain.Specifications.BlockServices
{
    /// <summary>
    /// Loads booking stays with everything a block file needs. Grouping by destination + check-in
    /// happens in memory afterwards — the specification pipeline has no projection or GroupBy support,
    /// and <see cref="Booking.Pax"/> / <see cref="Booking.LeadGuestName"/> are computed C# properties
    /// EF cannot translate anyway.
    /// </summary>
    public class BlockStaysSpec : BaseSpecifications<BookingStay>
    {
        // Every stay in the window, optionally narrowed to one destination.
        public BlockStaysSpec(DateOnly? from, DateOnly? to, int? destinationId)
            : base(s => (from == null || s.CheckInDate >= from)
                     && (to == null || s.CheckInDate <= to)
                     && (destinationId == null || s.DestinationId == destinationId))
        {
            AddIncludes();
            SetOrderBy(s => s.CheckInDate);
        }

        // The stays of exactly one block.
        public BlockStaysSpec(int destinationId, DateOnly checkIn)
            : base(s => s.DestinationId == destinationId && s.CheckInDate == checkIn)
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("Destination");
            AddInclude("Booking.Guests");
            AddInclude("Booking.Rooms");
            AddInclude("Booking.Package");
        }
    }
}
