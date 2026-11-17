namespace Travel.Domain.Services.Reports
{
    /// <summary>
    /// What each booking cost to run, in the base currency (USD). The one owner of that conversion,
    /// shared by the Revenue report and the Home dashboard so the two can never disagree.
    ///
    /// <para>
    /// <b>Costs are not filtered by date.</b> The booking is the unit: a trip that ran in August whose
    /// hotel was settled in July still cost that money, and filtering by <c>SpentOn</c> would flatter
    /// August's margin while orphaning July's spend.
    /// </para>
    /// </summary>
    public interface IBookingCostService
    {
        /// <summary>
        /// Every expense allocated to these bookings, both arrival-group shares and direct booking
        /// expenses. A booking with no allocations has no entry. Two queries regardless of count.
        /// </summary>
        Task<IReadOnlyDictionary<int, BookingCost>> LoadCostsAsync(IReadOnlyCollection<int> bookingIds);
    }

    /// <param name="AmountUsd">Allocated cost in the base currency, unrounded.</param>
    /// <param name="UnratedCount">
    /// Allocations left out for want of an exchange rate. They are counted and surfaced, never folded
    /// in at face value — treating EGP as USD would understate the cost ~48-fold.
    /// </param>
    public sealed record BookingCost(decimal AmountUsd, int UnratedCount);
}
