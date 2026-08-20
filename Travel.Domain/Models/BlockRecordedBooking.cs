namespace Travel.Domain.Models
{
    /// <summary>
    /// What the supplier was told about one booking in one arrival group — the record that makes
    /// <see cref="Enums.BlockStatus.NeedsAmendment"/> derivable.
    ///
    /// <para>
    /// Rows exist while the group's stored status is either
    /// <see cref="Enums.BlockStatus.InProgress"/> (what the booking request described) or
    /// <see cref="Enums.BlockStatus.Confirmed"/> (what the supplier confirmed): they are written when
    /// the operator saves one of those two and deleted when the block moves to
    /// <see cref="Enums.BlockStatus.NotBooked"/>. Either kind can go out of date, and the block reads
    /// <see cref="Enums.BlockStatus.NeedsAmendment"/> when it does.
    /// </para>
    ///
    /// <para>
    /// A booking is <see cref="Enums.BookingStatus.Confirmed"/> only when every group on its itinerary
    /// is stored as <see cref="Enums.BlockStatus.Confirmed"/> and holds a row for it that still
    /// matches its current facts — a request record confirms nothing.
    /// </para>
    ///
    /// <para>
    /// It stores a <i>fingerprint</i> of the supplier-relevant facts rather than a timestamp on purpose:
    /// no <c>Booking.UpdatedAt</c> column is needed, and fixing a typo in the notes or filling in a
    /// flight number doesn't falsely re-open a settled block.
    /// </para>
    /// </summary>
    public class BlockRecordedBooking : BaseModel
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

        /// <summary>Canonical room tally, e.g. "1x2|2x1" — built by <c>BlockSnapshot.RoomsFingerprint</c>.</summary>
        public string RoomsFingerprint { get; set; } = string.Empty;

        /// <summary>When this record was taken — the moment the operator saved the block's status.</summary>
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    }
}
