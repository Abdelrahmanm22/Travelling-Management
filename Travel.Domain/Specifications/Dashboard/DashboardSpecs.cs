using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Dashboard
{
    /// <summary>
    /// Every non-cancelled booking on the ground at some point between <paramref name="from"/> and
    /// <paramref name="to"/>, by the coalesced flight dates (<c>FlightArrivalDate ?? ArrivalDate</c>) —
    /// the window behind the dashboard's live operations tiles, the arrivals chart and the
    /// missing-flight list. Guests only, for <c>LeadGuestName</c>: PAX is a stored breakdown.
    /// </summary>
    public class DashboardOperationsBookingsSpec : BaseSpecifications<Booking>
    {
        public DashboardOperationsBookingsSpec(DateOnly from, DateOnly to)
            : base(b => b.Status != BookingStatus.Cancelled
                     && (b.FlightArrivalDate ?? b.ArrivalDate) <= to
                     && (b.FlightDepartureDate ?? b.DepartureDate) >= from)
        {
            AddInclude("Guests");

            // One collection — a plain query, per the per-spec splitting rule.
            UseSingleQuery();

            SetOrderBy(b => b.ArrivalDate);
        }
    }

    /// <summary>
    /// Bookings by package <c>ArrivalDate</c>, every status, no includes — the status donut and the
    /// "arriving this period" count read only scalar columns.
    /// </summary>
    public class DashboardBookingStatusSpec : BaseSpecifications<Booking>
    {
        public DashboardBookingStatusSpec(DateOnly from, DateOnly to)
            : base(b => b.ArrivalDate >= from && b.ArrivalDate <= to)
        {
        }
    }

    /// <summary>
    /// Every non-cancelled booking with what its balance reads — <c>GrandTotal</c> and
    /// <c>PaidTotal</c> are computed in C#, so the includes are load-bearing and their absence is
    /// silent. 🔴 <c>"Stays.Rates"</c>, not <c>"Stays"</c>: without the rates every booking reads
    /// unpriced and Outstanding drops to zero.
    /// </summary>
    public class DashboardBalanceSpec : BaseSpecifications<Booking>
    {
        public DashboardBalanceSpec()
            : base(b => b.Status != BookingStatus.Cancelled)
        {
            AddInclude("Stays.Rates");  // ExtraNightsTotal, HasUnpricedExtraNights
            AddInclude("Tours");        // ToursTotal
            AddInclude("Transactions"); // PaidTotal

            // Three sibling collections over every booking — split.
            UseSplitQuery();
        }
    }

    /// <summary>Client payments and refunds by <c>PaidOn</c>, no includes.</summary>
    public class DashboardTransactionsSpec : BaseSpecifications<BookingTransaction>
    {
        public DashboardTransactionsSpec(DateOnly from, DateOnly to)
            : base(t => t.PaidOn >= from && t.PaidOn <= to)
        {
        }
    }

    /// <summary>Expenses by <c>SpentOn</c>, reaching the paying safe for its currency.</summary>
    public class DashboardExpensesSpec : BaseSpecifications<BlockExpense>
    {
        public DashboardExpensesSpec(DateOnly from, DateOnly to)
            : base(e => e.SpentOn >= from && e.SpentOn <= to)
        {
            AddInclude("SafeEntry.Safe");
        }
    }
}
