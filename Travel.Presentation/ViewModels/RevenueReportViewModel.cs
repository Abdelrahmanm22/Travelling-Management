using System.ComponentModel.DataAnnotations;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// One booking's line in the Revenue report — the single shape the table <b>and</b> the .xlsx
    /// render, built once by <c>ReportController</c>, so the screen and the sheet cannot drift.
    /// </summary>
    /// <param name="SalePrice">
    /// <c>Booking.GrandTotal</c> — pax + extra nights + tours. <b>Null while the booking is unpriced</b>,
    /// never 0: an unpriced trip and a free one must not read the same.
    /// </param>
    /// <param name="ExpensesBase">
    /// Every expense allocated to this booking, converted to the base currency and summed
    /// <b>unrounded</b> — only the displayed figure is rounded, so the column and the footer agree.
    /// </param>
    /// <param name="UnratedExpenseCount">
    /// Costs that had to be left out because their expense carries no exchange rate. Surfaced per row
    /// rather than dropped silently — a cost that vanishes makes a margin look better than it is.
    /// </param>
    public sealed record RevenueReportRow(
        int BookingId,
        string BookingCode,
        string LeadGuestName,
        DateOnly ArrivalDate,
        string PackageName,
        int Pax,
        decimal? SalePrice,
        decimal ExpensesBase,
        int UnratedExpenseCount,
        decimal? NetRevenue,
        decimal? MarginPercent)
    {
        public bool IsPriced => SalePrice is not null;

        public bool HasUnratedExpense => UnratedExpenseCount > 0;
    }

    public class RevenueReportViewModel
    {
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        /// <summary>
        /// Distinguishes a first visit from a deliberately cleared box, so the default date can't
        /// silently refill itself. Same idiom as the Flights report and every filtered list.
        /// </summary>
        public bool Applied { get; set; }

        public IReadOnlyList<RevenueReportRow> Rows { get; set; } = Array.Empty<RevenueReportRow>();

        public bool HasFilters => From is not null || To is not null;

        public int BookingCount => Rows.Count;

        public int PaxCount => Rows.Sum(r => r.Pax);

        /// <summary>
        /// Only priced rows contribute — an unpriced booking's revenue is unknown, and treating its
        /// null as 0 would report it as a free trip.
        /// </summary>
        public decimal TotalSale => Rows.Sum(r => r.SalePrice ?? 0m);

        /// <summary>
        /// <b>Every</b> row, including unpriced ones. Their cost is a real figure that was really paid,
        /// and — the deciding reason — the Expenses column on screen shows it on those rows, so a
        /// footer that skipped them would not equal the column above it. A total that doesn't
        /// reconcile with its own column is worse than one that needs a footnote, and
        /// <see cref="UnpricedCount"/> supplies the footnote.
        /// </summary>
        public decimal TotalExpenses => Rows.Sum(r => r.ExpensesBase);

        /// <summary>
        /// Sale less expenses over the listed rows. When some are unpriced this is <b>understated</b>
        /// — their cost counts but their revenue can't — which is exactly what
        /// <see cref="UnpricedCount"/> exists to say out loud, on screen and in the sheet.
        /// </summary>
        public decimal TotalNet => TotalSale - TotalExpenses;

        public decimal? TotalMarginPercent =>
            TotalSale == 0m ? null : Math.Round(TotalNet / TotalSale * 100m, 1, MidpointRounding.AwayFromZero);

        public int UnpricedCount => Rows.Count(r => !r.IsPriced);

        public int UnratedExpenseCount => Rows.Sum(r => r.UnratedExpenseCount);
    }
}
