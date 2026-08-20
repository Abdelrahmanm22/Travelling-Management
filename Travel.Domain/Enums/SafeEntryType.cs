using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// What one movement in a money safe is. The direction is <b>derived</b> from this
    /// (<c>SafeEntry.SignedAmount</c>) rather than stored as a second enum or a signed amount —
    /// the same call <c>TransactionType</c> makes on the booking payment ledger.
    ///
    /// <para>
    /// <see cref="Expense"/> rows are written only by the expense flow. The ledger UI refuses to edit
    /// or delete them and points at the expense instead: a value only one code path may produce needs
    /// the other paths to say no out loud.
    /// </para>
    /// </summary>
    public enum SafeEntryType
    {
        [Display(Name = "Deposit")]
        Deposit = 1,

        [Display(Name = "Withdrawal")]
        Withdrawal = 2,

        [Display(Name = "Expense")]
        Expense = 3,
    }
}
