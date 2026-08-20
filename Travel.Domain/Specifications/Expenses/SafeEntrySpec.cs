using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Expenses
{
    /// <summary>
    /// One safe's ledger, or a single entry. Includes reach as deep as the ledger view reads —
    /// <c>Expense</c> so an expense row can name and link its expense, <c>Attachments</c> so the
    /// receipts column isn't silently empty.
    /// </summary>
    public class SafeEntrySpec : BaseSpecifications<SafeEntry>
    {
        public SafeEntrySpec(int id) : base(e => e.Id == id)
        {
            AddIncludes();
        }

        public SafeEntrySpec(int safeId, DateOnly? from, DateOnly? to, SafeEntryType? type)
            : base(e => e.SafeId == safeId
                     && (!from.HasValue || e.OccurredOn >= from.Value)
                     && (!to.HasValue || e.OccurredOn <= to.Value)
                     && (!type.HasValue || e.Type == type.Value))
        {
            AddIncludes();
            SetOrderByDesc(e => e.OccurredOn);
        }

        private void AddIncludes()
        {
            AddInclude("Safe");
            AddInclude("Expense");
            AddInclude("Attachments");
        }
    }

    /// <summary>
    /// Every entry, no includes — one query behind the whole safes grid, grouped in memory into a
    /// balance per safe. Deliberately not one query per card.
    /// </summary>
    public class SafeBalanceSpec : BaseSpecifications<SafeEntry>
    {
    }

    /// <summary>Whether a safe has any movement at all — guards delete and currency changes.</summary>
    public class SafeEntriesOfSafeSpec : BaseSpecifications<SafeEntry>
    {
        public SafeEntriesOfSafeSpec(int safeId) : base(e => e.SafeId == safeId)
        {
        }
    }
}
