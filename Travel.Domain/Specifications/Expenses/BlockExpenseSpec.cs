using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Expenses
{
    /// <summary>
    /// A block expense with everything any view of it reads.
    ///
    /// <para>
    /// 🔴 Every constructor shares <see cref="AddIncludes"/> deliberately. Two specifications over the
    /// same rows at different depths is exactly the bug that made a booking's Details page print "—"
    /// for a receipt the transactions list showed correctly: <b>an include must reach as deep as the
    /// view reads</b>, and the surest way to keep that true is for there to be only one include set.
    /// </para>
    /// </summary>
    public class BlockExpenseSpec : BaseSpecifications<BlockExpense>
    {
        public BlockExpenseSpec(int id) : base(e => e.Id == id)
        {
            AddIncludes();
        }

        public BlockExpenseSpec(
            DateOnly? from, DateOnly? to, int? safeId, int? expenseTypeId, int? destinationId)
            : base(e => (!from.HasValue || e.SpentOn >= from.Value)
                     && (!to.HasValue || e.SpentOn <= to.Value)
                     && (!safeId.HasValue || (e.SafeEntry != null && e.SafeEntry.SafeId == safeId.Value))
                     && (!expenseTypeId.HasValue || e.ExpenseTypeId == expenseTypeId.Value)
                     && (!destinationId.HasValue || e.Groups.Any(g => g.DestinationId == destinationId.Value)))
        {
            AddIncludes();
            SetOrderByDesc(e => e.SpentOn);
        }

        /// <summary>Every expense touching one arrival group — the card on the block's Details page.</summary>
        public BlockExpenseSpec(int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId)
            : base(e => e.Groups.Any(g => g.DestinationId == destinationId
                                       && g.CheckInDate == checkIn
                                       && g.Category == category
                                       && g.PrivateBookingId == privateBookingId))
        {
            AddIncludes();
            SetOrderByDesc(e => e.SpentOn);
        }

        /// <summary>Whether any expense still uses a type — the delete guard on the catalogue.</summary>
        public static BaseSpecifications<BlockExpense> UsingType(int expenseTypeId) =>
            new(e => e.ExpenseTypeId == expenseTypeId);

        private void AddIncludes()
        {
            AddInclude("ExpenseType");
            AddInclude("Rates");
            AddInclude("Groups");
            AddInclude("Groups.Destination");
            AddInclude("Groups.Bookings");
            AddInclude("SafeEntry");
            AddInclude("SafeEntry.Safe");
            AddInclude("SafeEntry.Attachments");
        }
    }
}
