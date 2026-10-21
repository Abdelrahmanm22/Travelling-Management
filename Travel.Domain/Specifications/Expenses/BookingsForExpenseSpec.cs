using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Expenses
{
    /// <summary>
    /// Bookings with exactly what a booking-target expense reads: the pax breakdown, the room tally,
    /// the whole itinerary (for <see cref="Booking.TripNights"/>) and the guests (for the lead name).
    ///
    /// <para>
    /// 🔴 <b>No status filter</b>, unlike <c>BlockStaysSpec</c>. The operator ticked these bookings by
    /// hand — a cancelled one is how a cancellation fee is recorded — so dropping it here would make
    /// the expense silently skip the booking it was meant for.
    /// </para>
    /// </summary>
    public class BookingsForExpenseSpec : BaseSpecifications<Booking>
    {
        public BookingsForExpenseSpec(IReadOnlyCollection<int> bookingIds)
            : base(b => bookingIds.Contains(b.Id))
        {
            AddInclude("Stays");
            AddInclude("Rooms");
            AddInclude("Guests");
        }
    }
}
