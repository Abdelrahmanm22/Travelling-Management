using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// The <c>Booking/Index</c> list — only what a row renders. <see cref="BookingWithDetailsSpec"/> is the
    /// whole Details graph (rooms, attachments, tour guests, receipts…), none of which the list shows.
    /// <para>
    /// 🔴 <b>The three money includes are load-bearing, and their absence is silent.</b>
    /// <see cref="Booking.GrandTotal"/> and <see cref="Booking.PaidTotal"/> are computed in C# over
    /// <c>Stays.Rates</c>, <c>Tours</c> and <c>Transactions</c>; drop one and the Total and Payment
    /// columns read too small (or "—") with no error. A new column on the list must add its include here.
    /// </para>
    /// </summary>
    public class BookingListSpec : BaseSpecifications<Booking>
    {
        // Any of the four filters may be null, meaning "no constraint".
        // guestArranged is a plain bool: false means "don't narrow", not "those without".
        public BookingListSpec(DateOnly? arrivalFrom = null, DateOnly? arrivalTo = null, int? packageId = null,
                               BookingStatus? status = null, bool guestArranged = false)
            : base(b => (arrivalFrom == null || b.ArrivalDate >= arrivalFrom)
                     && (arrivalTo == null || b.ArrivalDate <= arrivalTo)
                     && (packageId == null || b.PackageId == packageId)
                     && (status == null || b.Status == status)
                     // Any() over a navigation on a bool column IS translatable — unlike Pax or
                     // GrandTotal, which are C# and must never reach Criteria.
                     && (!guestArranged || b.Stays.Any(s => s.ExtraNightsArrangedByGuest)))
        {
            AddInclude("Package");
            AddInclude("Guests");      // LeadGuestName
            // "Stays.Rates", not "Stays": ExtraNightsTotal and HasUnpricedExtraNights read the rates —
            // without them every total renders "—". Stays alone also drive the guest-arranged icon.
            AddInclude("Stays.Rates");
            AddInclude("Tours");       // ToursTotal — Pax x UnitPrice are columns on the row, no Tour/Guests needed
            AddInclude("Transactions"); // PaidTotal — receipts aren't shown on the list

            // Four sibling collections over every booking.
            UseSplitQuery();

            SetOrderByDesc(b => b.ArrivalDate);
        }
    }
}
