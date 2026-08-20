using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// Bookings with their stays and rooming — the minimum needed to re-derive a booking's status from
    /// the arrival groups it belongs to. Rooms are here because the rule is no longer "are the blocks
    /// confirmed?" but "does each block's confirmation snapshot still match this booking?", and the
    /// snapshot covers rooms, PAX and nights (see <c>BlockSnapshot</c>). Still deliberately lighter
    /// than <see cref="BookingWithDetailsSpec"/>: a single block confirmation recalculates every
    /// booking in the group, and pulling guests, package and attachments for each would be wasted work.
    /// </summary>
    public class BookingsForStatusSpec : BaseSpecifications<Booking>
    {
        public BookingsForStatusSpec(IReadOnlyCollection<int> bookingIds)
            : base(b => bookingIds.Contains(b.Id))
        {
            AddIncludes();
        }

        public BookingsForStatusSpec(int bookingId) : base(b => b.Id == bookingId)
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("Stays.Destination");
            AddInclude("Rooms");
        }
    }
}
