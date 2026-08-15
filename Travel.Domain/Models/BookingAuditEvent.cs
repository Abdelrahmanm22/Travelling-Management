using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One save against a booking, and who made it. The header for a group of
    /// <see cref="BookingAuditChange"/> rows — grouped by this row rather than by timestamp, because
    /// two saves landing in the same second must not merge into one entry.
    /// </summary>
    public class BookingAuditEvent : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Snapshot, no FK to AspNetUsers — same reasoning as
        /// <see cref="BookingTransaction.CreatedById"/>. A trail that goes blank when someone leaves
        /// the company is not a trail.
        /// </summary>
        public string? UserId { get; set; }
        public string? UserName { get; set; }

        public AuditAction Action { get; set; }

        /// <summary>One-line description, e.g. "Booking updated (3 changes)".</summary>
        public string Summary { get; set; } = string.Empty;

        public ICollection<BookingAuditChange> Changes { get; set; } = new List<BookingAuditChange>();
    }
}
