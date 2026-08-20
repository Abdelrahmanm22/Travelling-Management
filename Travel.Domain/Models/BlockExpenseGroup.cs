namespace Travel.Domain.Models
{
    /// <summary>
    /// One arrival group an expense was applied to, identified the way every arrival group in this app
    /// is: <see cref="DestinationId"/> + <see cref="CheckInDate"/>.
    ///
    /// <para>
    /// The counts are snapshotted alongside the fingerprint so the change flag can answer <i>"what did
    /// it look like when we paid?"</i> and not merely <i>"did something change?"</i> — the exact
    /// complaint that produced the block confirmation snapshot.
    /// </para>
    /// </summary>
    public class BlockExpenseGroup : BaseModel
    {
        public int ExpenseId { get; set; }
        public BlockExpense Expense { get; set; } = null!;

        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        /// <summary>Snapshot — the FK is Restrict so it can't be deleted, but it can still be renamed.</summary>
        public string DestinationName { get; set; } = null!;

        public DateOnly CheckInDate { get; set; }

        /// <summary>Frozen. Equals <c>Σ Bookings.Amount</c> by construction.</summary>
        public decimal Amount { get; set; }

        public int BookingCount { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        /// <summary>
        /// What the per-pax division actually divided by, under the expense's own IncludeChildren /
        /// IncludeInfants snapshot. Derivable from the three counts above plus those flags — stored
        /// anyway so "what did we divide by?" is answerable directly.
        /// </summary>
        public int EligiblePax { get; set; }

        /// <summary>Canonical "1x2|2x1" for the whole group, via <c>BlockConfirmation.RoomsFingerprint</c>.</summary>
        public string RoomsFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// The group as it stood when the expense was applied — see <c>ExpenseFingerprint</c>. Compared
        /// against a freshly derived one on every read to raise the "block has changed" flag.
        /// </summary>
        public string AppliedFingerprint { get; set; } = string.Empty;

        public ICollection<BlockExpenseBooking> Bookings { get; set; } = new List<BlockExpenseBooking>();
    }
}
