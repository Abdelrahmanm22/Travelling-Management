using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// The minimum a booking needs for <see cref="Booking.RequiresDomesticFlight"/> to be answerable,
    /// and for its arrival groups to be identified: its full itinerary, nothing else. The document
    /// itself hangs off the group (<see cref="BlockAttachment"/>), not the booking, so the caller
    /// resolves "is one missing?" from these stays' groups.
    /// <para>
    /// This deliberately exists as its own query instead of one more include on <c>BlockStaysSpec</c>.
    /// That spec is windowed by date and optionally narrowed to one destination, so its
    /// <c>Booking.Stays</c> would be the <i>filtered</i> stays — a Cairo-only filter would hide the
    /// Hurghada leg and the "2+ destinations" half of the rule would silently read false.
    /// </para>
    /// </summary>
    public class BookingDocsSpec : BaseSpecifications<Booking>
    {
        public BookingDocsSpec(List<int> bookingIds) : base(b => bookingIds.Contains(b.Id))
        {
            AddInclude("Stays");
        }
    }
}
