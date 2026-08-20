using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.Expenses
{
    /// <summary>One booking's contribution to a group's fingerprint.</summary>
    public readonly record struct ExpenseFingerprintLine(
        int BookingId, int Adults, int Children, int Infants, int Nights, string Rooms);

    /// <summary>
    /// The single owner of "has this arrival group changed since we paid for it?", kept pure and in one
    /// place so the write side (applying and recalculating an expense) and the read side (raising the
    /// flag) can't drift on what counts as a change — the same shape as
    /// <see cref="BlockSnapshot"/>.
    ///
    /// <para>
    /// It canonicalises <b>exactly what the two formulas read and nothing else</b>: booking identity
    /// (swapping one booking for an otherwise identical one must flag, because the breakdown names it),
    /// the pax breakdown (the per-pax divisor), that booking's nights <i>in this group</i> (the
    /// per-room multiplier), and its room tally (the per-room base). Hotel, flights, guest names,
    /// category, notes and package are deliberately absent — an operator filling in a flight number two
    /// weeks later must not re-open a settled expense.
    /// </para>
    /// </summary>
    public static class ExpenseFingerprint
    {
        /// <summary>e.g. "12:2/1/0:3:1x1|2x1" joined with ';' — ordinally sorted, so row order can never produce a false flag.</summary>
        public static string Of(IEnumerable<ExpenseFingerprintLine> lines) =>
            string.Join(";", lines
                .Select(l => $"{l.BookingId}:{l.Adults}/{l.Children}/{l.Infants}:{l.Nights}:{l.Rooms}")
                .OrderBy(s => s, StringComparer.Ordinal));

        /// <summary>From loaded stays — one per booking, already de-duplicated by the caller.</summary>
        public static string FromStays(IEnumerable<BookingStay> stays) =>
            Of(stays.Select(s => new ExpenseFingerprintLine(
                s.BookingId,
                s.Booking.Adults,
                s.Booking.Children,
                s.Booking.Infants,
                s.Nights,
                BlockSnapshot.RoomsFingerprint(s.Booking.Rooms))));

        /// <summary>From the block file's flattened booking lines — what the calculator has in hand.</summary>
        public static string FromBookingLines(IEnumerable<BlockBookingLine> lines) =>
            Of(lines.Select(l => new ExpenseFingerprintLine(
                l.BookingId,
                l.Adults,
                l.Children,
                l.Infants,
                l.Nights,
                BlockSnapshot.RoomsFingerprint(l.Rooms))));

        public static bool Matches(string? applied, string? current) =>
            string.Equals(applied ?? string.Empty, current ?? string.Empty, StringComparison.Ordinal);
    }
}
