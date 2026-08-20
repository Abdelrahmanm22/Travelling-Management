using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>
    /// A block's status as it actually reads now, plus which of its bookings the supplier's
    /// confirmation still covers.
    /// </summary>
    public sealed record BlockResolution(BlockStatus Status, IReadOnlySet<int> CoveredBookingIds)
    {
        public static BlockResolution Of(BlockStatus status) =>
            new(status, new HashSet<int>());
    }

    /// <summary>
    /// The single owner of the "does what we told the supplier still hold?" rule, kept pure and in
    /// one place so the read side (<c>BlockServiceQueryService</c>) and the write side
    /// (<c>BlockStatusService</c>) can't drift on what counts as a change.
    ///
    /// <para>
    /// Supplier-relevant means <b>rooms, PAX breakdown and nights</b> — what the supplier actually
    /// blocks. Notes, nationality, guest names, hotel and flight details deliberately do not flag: an
    /// operator filling in a flight number two weeks later must not re-open a settled block.
    /// </para>
    /// </summary>
    public static class BlockSnapshot
    {
        /// <summary>
        /// Canonical room tally for one booking, e.g. "1x2|2x1". Ordered by the RoomType enum so the
        /// same rooming always produces the same string regardless of row order.
        /// </summary>
        public static string RoomsFingerprint(IEnumerable<BookingRoom> rooms) =>
            string.Join("|", rooms
                .GroupBy(r => r.Type)
                .Select(g => new { Type = g.Key, Quantity = g.Sum(r => r.Quantity) })
                .Where(t => t.Quantity > 0)
                .OrderBy(t => t.Type)
                .Select(t => $"{(int)t.Type}x{t.Quantity}"));

        /// <summary>
        /// The same canonical string from an already-tallied list — what a <see cref="BlockBookingLine"/>
        /// carries. Kept here rather than re-spelled elsewhere so this class stays the one owner of the
        /// "1x2|2x1" format; the block expense fingerprint reuses it.
        /// </summary>
        public static string RoomsFingerprint(IEnumerable<RoomTally> rooms) =>
            string.Join("|", rooms
                .Where(t => t.Quantity > 0)
                .OrderBy(t => t.Type)
                .Select(t => $"{(int)t.Type}x{t.Quantity}"));

        /// <summary>Whether a stored record still describes the booking as it stands now.</summary>
        public static bool Matches(BlockRecordedBooking snapshot, Booking booking, int nights) =>
            snapshot.Adults == booking.Adults
            && snapshot.Children == booking.Children
            && snapshot.Infants == booking.Infants
            && snapshot.Nights == nights
            && snapshot.RoomsFingerprint == RoomsFingerprint(booking.Rooms);

        /// <summary>Builds the record written when the operator saves a block's status.</summary>
        public static BlockRecordedBooking Take(Booking booking, int nights) =>
            new()
            {
                BookingId = booking.Id,
                Adults = booking.Adults,
                Children = booking.Children,
                Infants = booking.Infants,
                Nights = nights,
                RoomsFingerprint = RoomsFingerprint(booking.Rooms),
                RecordedAt = DateTime.UtcNow,
            };

        /// <summary>
        /// The status a group actually reads as. A stored <see cref="BlockStatus.InProgress"/> or
        /// <see cref="BlockStatus.Confirmed"/> holds while the record still describes every booking now
        /// in the group and describes nothing that has left, and reads
        /// <see cref="BlockStatus.NeedsAmendment"/> otherwise — the request we sent and the
        /// confirmation we got back can both go out of date.
        /// </summary>
        /// <param name="stays">One stay per booking — already de-duplicated by the caller.</param>
        public static BlockResolution Resolve(
            BlockStatus stored,
            IReadOnlyList<BookingStay> stays,
            IReadOnlyCollection<BlockRecordedBooking> snapshots)
        {
            // NotBooked has told the supplier nothing, so it has nothing that can go out of date.
            if (stored is not (BlockStatus.InProgress or BlockStatus.Confirmed))
                return BlockResolution.Of(stored);

            // Blocks saved as In progress before records were kept for that status have nothing to
            // compare against. "Never recorded" is not "changed" — they start flagging on the next
            // status save, which writes the record.
            if (stored == BlockStatus.InProgress && snapshots.Count == 0)
                return BlockResolution.Of(stored);

            var covered = new HashSet<int>();
            foreach (var stay in stays)
            {
                var snapshot = snapshots.FirstOrDefault(s => s.BookingId == stay.BookingId);
                if (snapshot is not null && Matches(snapshot, stay.Booking, stay.Nights))
                    covered.Add(stay.BookingId);
            }

            // A booking that left the group is just as much an amendment as one that joined — the
            // supplier is still holding rooms nobody needs.
            var current = stays.Select(s => s.BookingId).ToHashSet();
            var stale = snapshots.Any(s => !current.Contains(s.BookingId));

            var matches = !stale && covered.Count == current.Count;

            // 🔴 "Covered" is a CONFIRMATION fact — Booking/Details reads it for the "N of M blocks
            // confirmed" line and the per-stay note. An In-progress block has recorded a request, not
            // a confirmation, so it covers nothing whether it still matches or not.
            var coveredIds = stored == BlockStatus.Confirmed ? covered : new HashSet<int>();

            return new BlockResolution(matches ? stored : BlockStatus.NeedsAmendment, coveredIds);
        }
    }
}
