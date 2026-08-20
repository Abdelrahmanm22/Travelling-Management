using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// Bookings whose trip ran inside the range — the Revenue report's rows.
    ///
    /// <para>
    /// The range is on <see cref="Booking.ArrivalDate"/>, not the booking's creation date: revenue is
    /// recognised when the service was delivered, which is what makes the sale price and the expenses
    /// paid to run that trip a like-for-like comparison. A trip sold in March for an August arrival
    /// belongs to August.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>The includes are load-bearing, and their absence is silent.</b>
    /// <see cref="Booking.GrandTotal"/> is computed in C# over loaded navigations and "reads 0 for the
    /// components whose navigation wasn't included" — so dropping <c>Stays</c> loses every extra-night
    /// charge and dropping <c>Tours</c> loses every tour. The report would still render and still
    /// total; it would just be too low, with nothing anywhere to say so.
    /// </para>
    ///
    /// <para>
    /// 🔴 Cancelled bookings are excluded, matching <see cref="FlightReportSpec"/>. As there, this
    /// <c>Criteria</c> decides what reaches a figure someone will act on — any further narrowing needs
    /// the same deliberate thought.
    /// </para>
    /// </summary>
    public class RevenueReportSpec : BaseSpecifications<Booking>
    {
        public RevenueReportSpec(DateOnly? from, DateOnly? to)
            : base(b => b.Status != BookingStatus.Cancelled
                     && (from == null || b.ArrivalDate >= from)
                     && (to == null || b.ArrivalDate <= to))
        {
            AddInclude("Stays");   // ExtraNightsTotal
            AddInclude("Tours");   // ToursTotal
            AddInclude("Guests");  // LeadGuestName
            AddInclude("Package"); // the Package column

            SetOrderBy(b => b.ArrivalDate);
        }
    }
}
