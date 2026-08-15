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
    }
}
