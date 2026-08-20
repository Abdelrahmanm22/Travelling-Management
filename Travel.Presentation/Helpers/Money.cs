using System.Globalization;

namespace Travel.Presentation.Helpers
{
    /// <summary>
    /// The single source of truth for how money reads, mirroring <see cref="StatusBadges"/>. The app
    /// prices every booking in one currency, so the code is a label rather than a stored column —
    /// changing it is this one const.
    ///
    /// <para>
    /// Amounts are formatted with <see cref="CultureInfo.InvariantCulture"/> so a server whose locale
    /// swaps the group and decimal separators can't make "1.300,00" and "1,300.00" appear on the same
    /// booking. Excel cells are written as <em>numbers</em> with <see cref="ExcelNumberFormat"/> — never
    /// through <see cref="Format"/> — so the recipient can sum them.
    /// </para>
    /// </summary>
    public static class Money
    {
        public const string Code = "USD";

        /// <summary>Number format for ClosedXML money cells.</summary>
        public const string ExcelNumberFormat = "#,##0.00";

        /// <summary>"USD 1,300.00", or "—" when there is no amount.</summary>
        public static string Format(decimal? amount) =>
            amount is { } a ? $"{Code} {Amount(a)}" : "—";

        /// <summary>The bare number, "1,300.00" — for tables and rows that carry the code in the header.</summary>
        public static string Amount(decimal amount) =>
            amount.ToString(ExcelNumberFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// "EGP 1,300.00". Bookings are priced in one currency app-wide (<see cref="Code"/>), but a
        /// money safe carries its own — so that side passes the safe's code explicitly. Formatting stays
        /// owned here either way; no view builds a money string by hand.
        /// </summary>
        public static string Format(decimal? amount, string? currency) =>
            amount is { } a ? $"{Normalize(currency)} {Amount(a)}" : "—";

        /// <summary>Falls back to <see cref="Code"/>, so a blank currency can never render as a bare number.</summary>
        public static string Normalize(string? currency) =>
            string.IsNullOrWhiteSpace(currency) ? Code : currency.Trim().ToUpperInvariant();
    }
}
