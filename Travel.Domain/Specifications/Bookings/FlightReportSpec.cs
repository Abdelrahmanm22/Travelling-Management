using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// Bookings with a flight movement inside the window — an arrival <b>or</b> a departure is enough,
    /// so a booking that arrives in one week and leaves in another appears on both reports.
    /// <para>
    /// A null flight date means "not recorded yet"; the package date stands in for it, in both the
    /// filter and the rendered cell, so a row can never land in a range its own date column disagrees with.
    /// </para>
    /// <para>
    /// 🔴 <b>This <c>Criteria</c> decides who reaches the airport list.</b> Anything added here silently
    /// removes passengers from a sheet that gets sent out — same caution as <c>BlockStaysSpec</c>.
    /// Cancelled bookings are excluded because nobody should be waiting at arrivals for them.
    /// </para>
    /// </summary>
    public class FlightReportSpec : BaseSpecifications<Booking>
    {
        public FlightReportSpec(DateOnly? from, DateOnly? to)
            : base(b => b.Status != BookingStatus.Cancelled
                     && (((from == null || (b.FlightArrivalDate ?? b.ArrivalDate) >= from)
                          && (to == null || (b.FlightArrivalDate ?? b.ArrivalDate) <= to))
                         ||
                         ((from == null || (b.FlightDepartureDate ?? b.DepartureDate) >= from)
                          && (to == null || (b.FlightDepartureDate ?? b.DepartureDate) <= to))))
        {
            AddInclude("Guests");
            SetOrderBy(b => b.FlightArrivalDate ?? b.ArrivalDate);
        }
    }
}
