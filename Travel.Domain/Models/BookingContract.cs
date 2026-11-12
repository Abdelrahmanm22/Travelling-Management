namespace Travel.Domain.Models
{
    /// <summary>
    /// A booking's contract draft and the record of its last generation. The draft never holds the
    /// client's CPF, RG or address — the serialised type has no property for them.
    /// </summary>
    public class BookingContract : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        /// <summary>The serialised draft: the edited document plus the booking facts it was built from.</summary>
        public string DraftJson { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? GeneratedAt { get; set; }

        /// <summary>Plain id + name snapshot, no FK — the <see cref="BookingTransaction.CreatedById"/> pattern.</summary>
        public string? GeneratedById { get; set; }
        public string? GeneratedByName { get; set; }
    }
}
