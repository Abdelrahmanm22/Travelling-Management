using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One entry in a booking's payment ledger. Installments are not a separate concept — an
    /// installment IS one of these, and the transaction log is the filterable view over them.
    /// </summary>
    public class BookingTransaction : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public TransactionType Type { get; set; } = TransactionType.Payment;

        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

        /// <summary>Always positive; <see cref="Type"/> decides which way it counts.</summary>
        public decimal Amount { get; set; }

        /// <summary>Signed contribution to the booking's paid total.</summary>
        public decimal SignedAmount => Type == TransactionType.Refund ? -Amount : Amount;

        /// <summary>When the money actually moved — not when the row was typed.</summary>
        public DateOnly PaidOn { get; set; }

        /// <summary>Transfer reference, card authorisation, receipt number.</summary>
        public string? Reference { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who logged it. Deliberately a plain string with a name snapshot and NO foreign key to
        /// AspNetUsers: a second FK into this table would create a cascade path (SQL Server 1785),
        /// AppUser's string Id puts it out of reach of IGenericRepository anyway, and the ledger must
        /// still read correctly after the user who typed it is deleted.
        /// </summary>
        public string? CreatedById { get; set; }
        public string? CreatedByName { get; set; }

        /// <summary>Receipts and proof of transfer.</summary>
        public ICollection<TransactionAttachment> Attachments { get; set; } = new List<TransactionAttachment>();
    }
}
