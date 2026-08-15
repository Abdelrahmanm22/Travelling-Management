namespace Travel.Domain.Models
{
    /// <summary>
    /// One stay block within a <see cref="Booking"/> (the persisted, editable source of truth): a
    /// destination the guest stays at from <see cref="CheckInDate"/> for <see cref="Nights"/> nights.
    /// </summary>
    public class BookingStay : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        public DateOnly CheckInDate { get; set; }
        public int Nights { get; set; }

        /// <summary>
        /// Nights the generator gave this row at the package's sold length. 0 for a row the operator
        /// added by hand, so the whole stay is chargeable. Never typed — it is written whenever the
        /// schedule is generated or regenerated, and posted back unchanged by the form.
        /// </summary>
        public int BaselineNights { get; set; }

        /// <summary>Per-night rate for the nights beyond <see cref="BaselineNights"/>. Null until priced.</summary>
        public decimal? ExtraNightPrice { get; set; }

        /// <summary>Chargeable nights on this row — derived from the schedule, never stored.</summary>
        public int ExtraNights => Math.Max(0, Nights - BaselineNights);

        public decimal ExtraNightsTotal => ExtraNights * (ExtraNightPrice ?? 0m);

        /// <summary>Zero-based position of this block within the booking.</summary>
        public int SortOrder { get; set; }

        /// <summary>Snapshot of the destination's cruise flag at generation time.</summary>
        public bool IsCruise { get; set; }

        /// <summary>True when the operator changed this block away from what the generator produced.</summary>
        public bool IsOverridden { get; set; }

        /// <summary>Optional pinned hotel for this block.</summary>
        public string? HotelName { get; set; }

        public string? Notes { get; set; }
    }
}
