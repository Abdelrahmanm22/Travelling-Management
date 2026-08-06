using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// Bookings with just their stays — the minimum needed to re-derive a booking's status from the
    /// arrival groups it belongs to. Deliberately lighter than <see cref="BookingWithDetailsSpec"/>:
    /// a single block confirmation recalculates every booking in the group, and pulling guests, rooms
    /// and attachments for each of them would be wasted work.
    /// </summary>
    public class BookingsForStatusSpec : BaseSpecifications<Booking>
    {
        public BookingsForStatusSpec(IReadOnlyCollection<int> bookingIds)
            : base(b => bookingIds.Contains(b.Id))
        {
            AddInclude("Stays.Destination");
        }

        public BookingsForStatusSpec(int bookingId) : base(b => b.Id == bookingId)
        {
            AddInclude("Stays.Destination");
        }
    }
}
