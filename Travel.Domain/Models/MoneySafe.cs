namespace Travel.Domain.Models
{
    /// <summary>
    /// A cash box the company pays out of — one in Egyptian pounds, one in dollars, as many as the
    /// operator opens. Each safe holds its own currency and its own ledger.
    ///
    /// <para>
    /// There is deliberately <b>no Balance column and no OpeningBalance column</b>. The balance is
    /// <c>Σ Entries.SignedAmount</c>, computed on read: a stored running total is a second source of
    /// truth that drifts the first time an entry is edited or deleted, and an opening balance says the
    /// same thing as the first Deposit entry. A safe opened mid-life records a Deposit called
    /// "Opening balance".
    /// </para>
    /// </summary>
    public class MoneySafe : BaseModel
    {
        public string Name { get; set; } = null!;

        /// <summary>
        /// ISO code — "EGP", "USD". A free three-letter string rather than an enum because safes are
        /// user-created: an enum would need a code change and a migration every time the operator opens
        /// a box in a currency nobody anticipated. There is no conversion and no cross-safe total
        /// anywhere, so the code is never anything but a label.
        ///
        /// <para>
        /// <b>Immutable once the safe holds any entry</b> — enforced in the controller, which reads it
        /// back from the stored row instead of the post. Changing it would silently re-denominate every
        /// historical figure in the ledger.
        /// </para>
        /// </summary>
        public string Currency { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>The soft retire. An inactive safe keeps its history but can no longer be paid out of.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Snapshot, no FK to AspNetUsers — see <see cref="BookingTransaction.CreatedById"/>.</summary>
        public string? CreatedById { get; set; }
        public string? CreatedByName { get; set; }

        public ICollection<SafeEntry> Entries { get; set; } = new List<SafeEntry>();
    }
}
