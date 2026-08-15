using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One field that moved, as it would have read on screen. Values are stored as strings on
    /// purpose: an audit row records what was displayed, not a typed value to compute with — which
    /// also spares the table a column per CLR type.
    /// </summary>
    public class BookingAuditChange : BaseModel
    {
        public int EventId { get; set; }
        public BookingAuditEvent Event { get; set; } = null!;

        /// <summary>"Booking", "Stay", "Guest", "Room", "Tour", "Transaction".</summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>Which one — "Hurghada", "Omar Ali". Empty for the booking itself.</summary>
        public string? EntityLabel { get; set; }

        public string Field { get; set; } = string.Empty;

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public AuditChangeKind Kind { get; set; } = AuditChangeKind.Modified;
    }
}
