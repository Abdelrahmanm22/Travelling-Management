using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One movement of money in or out of a safe — and the <b>only</b> ledger row there is. A manual
    /// deposit, a manual withdrawal, and the withdrawal behind a block expense are all this one shape.
    ///
    /// <para>
    /// That is what keeps the balance a single formula over a single table. The alternative — manual
    /// entries UNION expenses at query time — would make every balance, ledger page, filter and export
    /// join two shapes and keep two sort and pagination paths agreeing, with two places to get the sign
    /// wrong.
    /// </para>
    /// </summary>
    public class SafeEntry : BaseModel
    {
        public int SafeId { get; set; }
        public MoneySafe Safe { get; set; } = null!;

        public SafeEntryType Type { get; set; } = SafeEntryType.Deposit;

        /// <summary>Always positive; <see cref="Type"/> decides which way it counts.</summary>
        public decimal Amount { get; set; }

        /// <summary>Signed contribution to the safe's balance.</summary>
        public decimal SignedAmount => Type == SafeEntryType.Deposit ? Amount : -Amount;

        /// <summary>When the money actually moved — not when the row was typed.</summary>
        public DateOnly OccurredOn { get; set; }

        public string? Description { get; set; }

        /// <summary>Transfer reference, receipt number, invoice number.</summary>
        public string? Reference { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Snapshot, no FK to AspNetUsers — see <see cref="BookingTransaction.CreatedById"/>.</summary>
        public string? CreatedById { get; set; }
        public string? CreatedByName { get; set; }

        /// <summary>
        /// Set only on <see cref="SafeEntryType.Expense"/> rows; null on manual entries.
        ///
        /// <para>
        /// The foreign key lives here, and <see cref="BlockExpense"/> deliberately carries no SafeId.
        /// If it did, SQL Server would see two cascade paths from MoneySafes into SafeEntries
        /// (MoneySafe → SafeEntry, and MoneySafe → BlockExpense → SafeEntry) and reject the model with
        /// error 1785. It also removes any possibility of an expense and its own withdrawal disagreeing
        /// about which safe the money came out of.
        /// </para>
        /// </summary>
        public int? ExpenseId { get; set; }
        public BlockExpense? Expense { get; set; }

        /// <summary>
        /// Receipts and invoices. They hang off the entry rather than the expense because a receipt
        /// documents a <i>money movement</i>, and a manual withdrawal needs one just as much — so an
        /// expense's receipts are simply the ones on the entry it owns. One table, one folder, one
        /// upload path.
        /// </summary>
        public ICollection<SafeEntryAttachment> Attachments { get; set; } = new List<SafeEntryAttachment>();
    }
}
