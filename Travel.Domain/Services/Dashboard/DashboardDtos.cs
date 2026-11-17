using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.Dashboard
{
    /// <summary>A count of bookings and the travellers on them.</summary>
    public sealed record BookingCount(int Bookings, int Pax)
    {
        public static BookingCount None { get; } = new(0, 0);
    }

    public sealed record DailyArrivals(DateOnly Date, int Bookings, int Pax);

    /// <summary>A booking arriving soon with no international arrival flight number recorded.</summary>
    public sealed record MissingArrivalFlight(int BookingId, string Code, string LeadGuestName, DateOnly ArrivalDate, int Pax);

    /// <summary>
    /// Operations. "Arriving" and "departing" read the flight date when one is recorded, else the
    /// package date (<c>FlightArrivalDate ?? ArrivalDate</c>) — the Flights report's rule, because
    /// that is the day someone meets the guest. Cancelled bookings are excluded everywhere except
    /// <see cref="BookingStatuses"/>, which exists to show them.
    /// </summary>
    public sealed record OperationsStats(
        BookingCount ArrivingToday,
        BookingCount DepartingToday,
        BookingCount InCountry,
        // Bookings whose package ArrivalDate falls in the period — the date the Sales section uses too.
        BookingCount ArrivingInPeriod,
        // Arrival groups checking in during the period, by resolved status (NeedsAmendment included).
        IReadOnlyDictionary<BlockStatus, int> BlockStatuses,
        int BlockGroupCount,
        IReadOnlyList<DailyArrivals> ArrivalsByDay,
        IReadOnlyDictionary<BookingStatus, int> BookingStatuses,
        DateOnly AttentionFrom,
        DateOnly AttentionTo,
        // Groups in the attention window still Not booked, or reading Needs amendment.
        IReadOnlyList<BlockSummary> BlocksNeedingAction,
        IReadOnlyList<MissingArrivalFlight> MissingArrivalFlights,
        // Groups in the attention window with a booking needing a domestic flight that none ticks.
        IReadOnlyList<BlockSummary> MissingDomesticFlights);

    /// <param name="Expenses">Every listed booking's cost, priced or not — the Revenue report's footer rule.</param>
    public sealed record SalesFigures(
        int BookingCount,
        decimal Sale,
        decimal Expenses,
        decimal Net,
        decimal? MarginPercent,
        int UnpricedCount,
        int UnratedExpenseCount);

    public sealed record MonthlySales(int Year, int Month, SalesFigures Figures);

    /// <summary>
    /// Sales &amp; revenue, by the booking's <b>ArrivalDate</b> — exactly the Revenue report's rows and
    /// footer for the same range (same spec, same cost service, same margin rounding).
    /// </summary>
    public sealed record SalesStats(SalesFigures Period, IReadOnlyList<MonthlySales> Months);

    public sealed record MonthlyAmount(int Year, int Month, decimal Amount);

    /// <summary>Client payments (the booking ledger, USD) by <c>PaidOn</c>, plus live balances.</summary>
    public sealed record PaymentsStats(
        decimal PaymentsTotal,
        int PaymentCount,
        decimal RefundsTotal,
        int RefundCount,
        // Live: Σ Balance over priced, non-cancelled bookings that still owe something.
        decimal Outstanding,
        int OutstandingCount,
        // Live: non-cancelled bookings with no total yet, so they can't be part of Outstanding.
        int UnpricedCount,
        IReadOnlyList<MonthlyAmount> ReceivedByMonth)
    {
        public decimal Received => PaymentsTotal - RefundsTotal;
    }

    public sealed record SafeBalance(int SafeId, string Name, string Currency, decimal Balance, bool IsActive);

    public sealed record CurrencyTotal(string Currency, decimal Amount, int Count);

    /// <summary>Money safes and expenses. Every figure stays in its own currency — never summed across.</summary>
    public sealed record SafesStats(
        IReadOnlyList<SafeBalance> Safes,
        IReadOnlyList<CurrencyTotal> ExpensesByCurrency);
}
