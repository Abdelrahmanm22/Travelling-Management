using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.BlockServices
{
    /// <summary>
    /// Loads booking stays with everything a block file needs. Grouping by destination + check-in
    /// happens in memory afterwards — the specification pipeline has no projection or GroupBy support,
    /// and <see cref="Booking.Pax"/> / <see cref="Booking.LeadGuestName"/> are computed C# properties
    /// EF cannot translate anyway.
    /// <para>
    /// 🔴 Both constructors deliberately exclude <see cref="BookingStatus.Cancelled"/> bookings. Block
    /// Services is a derived read model, so narrowing which stays this returns silently changes which
    /// bookings reach a supplier's file — that is the intent here: a cancelled booking must not inflate
    /// a block's PAX or room tally. Any further narrowing needs the same deliberate consideration.
    /// The filter is EF-translatable because Status is a real column, unlike Pax/LeadGuestName.
    /// </para>
    /// </summary>
    public class BlockStaysSpec : BaseSpecifications<BookingStay>
    {
        // Every stay in the window, optionally narrowed to one destination.
        public BlockStaysSpec(DateOnly? from, DateOnly? to, int? destinationId)
            : base(s => s.Booking.Status != BookingStatus.Cancelled
                     && (from == null || s.CheckInDate >= from)
                     && (to == null || s.CheckInDate <= to)
                     && (destinationId == null || s.DestinationId == destinationId))
        {
            AddIncludes();
            SetOrderBy(s => s.CheckInDate);
        }

        // The stays of exactly one block.
        public BlockStaysSpec(int destinationId, DateOnly checkIn)
            : base(s => s.Booking.Status != BookingStatus.Cancelled
                     && s.DestinationId == destinationId
                     && s.CheckInDate == checkIn)
        {
            AddIncludes();
        }

        /// <summary>
        /// A superset of an arbitrary set of (destination, check-in) keys — EF can't translate a
        /// composite-key <c>Contains</c> — narrowed to the exact keys in memory by the caller.
        /// Carries only the rooming, which is all <c>BlockConfirmation.Resolve</c> reads; it lives here
        /// rather than in its own spec so the cancelled-booking filter above can never drift from it.
        /// </summary>
        public BlockStaysSpec(IReadOnlyCollection<int> destinationIds, DateOnly from, DateOnly to)
            : base(s => s.Booking.Status != BookingStatus.Cancelled
                     && destinationIds.Contains(s.DestinationId)
                     && s.CheckInDate >= from
                     && s.CheckInDate <= to)
        {
            AddInclude("Booking.Rooms");
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
