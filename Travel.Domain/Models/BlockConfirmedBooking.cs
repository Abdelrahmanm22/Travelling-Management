namespace Travel.Domain.Models
{
    /// <summary>
    /// What the supplier actually confirmed for one booking in one arrival group — the snapshot that
    /// makes <see cref="Enums.BlockStatus.NeedsAmendment"/> derivable.
    ///
    /// <para>
    /// Rows exist only while the group's stored status is <see cref="Enums.BlockStatus.Confirmed"/>:
    /// they are written when the operator confirms and deleted when the block moves off Confirmed.
    /// A booking is <see cref="Enums.BookingStatus.Confirmed"/> exactly when every group on its
    /// itinerary holds a row for it that still matches its current facts.
    /// </para>
    ///
    /// <para>
    /// It stores a <i>fingerprint</i> of the supplier-relevant facts rather than a timestamp on purpose:
    /// no <c>Booking.UpdatedAt</c> column is needed, and fixing a typo in the notes or filling in a
    /// flight number doesn't falsely re-open a confirmed block.
    /// </para>
    /// </summary>
    public class BlockConfirmedBooking : BaseModel
    {
        public int BlockServiceStatusId { get; set; }
        public BlockServiceStatus BlockServiceStatus { get; set; } = null!;

        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        /// <summary>Nights of the booking's stay in <i>this</i> group, not the whole trip.</summary>
        public int Nights { get; set; }

        /// <summary>Canonical room tally, e.g. "1x2|2x1" — built by <c>BlockConfirmation.RoomsFingerprint</c>.</summary>
        public string RoomsFingerprint { get; set; } = string.Empty;

        public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
    }
}
