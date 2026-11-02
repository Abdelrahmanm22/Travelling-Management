using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// Bookings carrying at least one extra tour whose trip <b>overlaps</b> the window — arrival on or
    /// before <c>to</c>, departure on or after <c>from</c>. A booked tour has no date of its own, so a
    /// guest who arrived last week and is still in Egypt this week must still appear on this week's sheet.
    /// <para>
    /// 🔴 <b>This <c>Criteria</c> decides who reaches the tour sheet.</b> Anything added here silently
    /// removes guests from it — same caution as <see cref="FlightReportSpec"/>. Cancelled bookings are
    /// excluded because nobody should be waiting at a tour pickup for them.
    /// </para>
    /// </summary>
    public class ToursReportSpec : BaseSpecifications<Booking>
    {
        public ToursReportSpec(DateOnly? from, DateOnly? to)
            : base(b => b.Status != BookingStatus.Cancelled
                     && b.Tours.Any()
                     && (from == null || b.DepartureDate >= from)
                     && (to == null || b.ArrivalDate <= to))
        {
            AddInclude("Guests");
            AddInclude("Package");
            AddInclude("Tours.Tour");
            // The link rows only — their GuestId is matched against Guests above.
            AddInclude("Tours.Guests");

            // Guests x tours x tour guests for every booking in the range.
            UseSplitQuery();

            SetOrderBy(b => b.ArrivalDate);
        }
    }
}
