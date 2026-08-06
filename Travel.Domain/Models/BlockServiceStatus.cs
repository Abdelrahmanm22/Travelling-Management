using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// The one persisted fact about an arrival group. Block Services itself stays a derived read model —
    /// groups are re-computed from live <see cref="BookingStay"/> rows on every request — so this side
    /// table is keyed on the same identity a group has: <see cref="DestinationId"/> +
    /// <see cref="CheckInDate"/>. Rows are created lazily on the first status change; a group with no
    /// row is <see cref="BlockStatus.NotBooked"/>. A row whose bookings have all moved away is harmless
    /// and simply re-applies if a booking returns to that destination and date.
    /// </summary>
    public class BlockServiceStatus : BaseModel
    {
        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        public DateOnly CheckInDate { get; set; }

        public BlockStatus Status { get; set; } = BlockStatus.NotBooked;

        /// <summary>Link to the email that requested this block from the supplier. Kept permanently.</summary>
        public string? RequestEmailLink { get; set; }

        /// <summary>
        /// Link to the supplier's confirmation email. Kept permanently and deliberately separate from
        /// <see cref="RequestEmailLink"/> — confirming a block must never overwrite the request thread.
        /// </summary>
        public string? ConfirmationEmailLink { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Plain string, no FK: AppUser has a string Id while IGenericRepository&lt;T&gt; is
        /// constrained to <see cref="BaseModel"/> (int Id), so a navigation here would buy nothing.
        /// </summary>
        public string? UpdatedByUserId { get; set; }

        /// <summary>Snapshot of the operator's name so the audit line survives the user being deleted.</summary>
        public string? UpdatedByName { get; set; }
    }
}
