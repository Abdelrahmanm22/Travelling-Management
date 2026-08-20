using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One room type's rate on a per-room expense — <b>per room, per night</b>.
    ///
    /// <para>
    /// A row per type rather than three nullable columns on the header: a fourth <see cref="RoomType"/>
    /// would then need no migration, and "the rates that were applied" renders as its own small table
    /// on the expense.
    /// </para>
    /// </summary>
    public class BlockExpenseRate : BaseModel
    {
        public int ExpenseId { get; set; }
        public BlockExpense Expense { get; set; } = null!;

        public RoomType RoomType { get; set; }

        public decimal Rate { get; set; }
    }
}
