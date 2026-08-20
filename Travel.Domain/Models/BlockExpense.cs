using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One cost paid out of one safe, applied to one or more arrival groups.
    ///
    /// <para>
    /// Every amount on it and beneath it is a <b>frozen snapshot</b> — the money genuinely left the
    /// safe at that figure. When the underlying blocks later change, the expense says so (the stored
    /// fingerprint stops matching) and offers Recalculate; it never re-derives itself silently, which
    /// would move a safe balance nobody touched.
    /// </para>
    /// </summary>
    public class BlockExpense : BaseModel
    {
        public int ExpenseTypeId { get; set; }
        public ExpenseType ExpenseType { get; set; } = null!;

        /// <summary>Snapshot — renaming a type must not restate an expense already paid.</summary>
        public string ExpenseTypeName { get; set; } = null!;

        /// <summary>
        /// Snapshots of the type's rule, and load-bearing: Recalculate has to re-derive under the rules
        /// the operator actually applied. Reading them live off <see cref="ExpenseType"/> would let an
        /// edit to the catalogue silently change what a frozen expense means.
        /// </summary>
        public ExpenseBasis Basis { get; set; }
        public bool IncludeChildren { get; set; }
        public bool IncludeInfants { get; set; }

        public DateOnly SpentOn { get; set; }

        /// <summary>
        /// The frozen total. Equals <c>Σ Groups.Amount</c> by construction — for a per-pax expense it is
        /// also exactly the lump sum the operator typed, which is why there is no second column for it.
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// How many units of the paying safe's currency equal one US dollar — "1 USD = 48.50 EGP" is
        /// stored as <c>48.50</c>, and the USD value of any amount on this expense is
        /// <c>amount / UnitsPerUsd</c>. <c>1</c> when the safe is already in USD.
        ///
        /// <para>
        /// Captured when the expense is recorded and never re-read from anywhere, so a later move in
        /// the rate cannot restate what an expense actually cost — the same snapshot rule as
        /// <see cref="ExpenseTypeName"/> and <c>BookingTour.UnitPrice</c>. It exists because bookings
        /// are priced in one currency app-wide while a safe carries its own, and the Revenue report
        /// has to subtract one from the other.
        /// </para>
        ///
        /// <para>
        /// <b>Nullable, and it must stay that way.</b> Null means "never recorded", which the Revenue
        /// report flags; <c>1</c> is a claim that the safe's currency is at parity with the dollar. A
        /// migration default of 1 would silently make every EGP expense read ~48× too cheap with no
        /// error anywhere.
        /// </para>
        /// </summary>
        public decimal? UnitsPerUsd { get; set; }

        public string? Description { get; set; }

        public string? Reference { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Snapshot, no FK to AspNetUsers — see <see cref="BookingTransaction.CreatedById"/>.</summary>
        public string? CreatedById { get; set; }
        public string? CreatedByName { get; set; }

        public DateTime? RecalculatedAt { get; set; }
        public string? RecalculatedByName { get; set; }

        /// <summary>Per-room rates only; a per-pax expense holds no rows here.</summary>
        public ICollection<BlockExpenseRate> Rates { get; set; } = new List<BlockExpenseRate>();

        public ICollection<BlockExpenseGroup> Groups { get; set; } = new List<BlockExpenseGroup>();

        /// <summary>
        /// The withdrawal this expense made. One expense is exactly one entry; the foreign key lives on
        /// the entry — see <see cref="SafeEntry.ExpenseId"/> for why the safe is reached through it
        /// rather than stored here.
        /// </summary>
        public SafeEntry? SafeEntry { get; set; }
    }
}
