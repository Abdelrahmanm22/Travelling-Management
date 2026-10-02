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

        /// <summary>
        /// Per-room-type rates for the nights beyond <see cref="BaselineNights"/>. Empty until priced.
        /// Hotels quote per room type, so the charge is built from these rather than one blended figure.
        /// </summary>
        public ICollection<BookingStayRate> Rates { get; set; } = new List<BookingStayRate>();

        /// <summary>
        /// The guest booked these extra nights themselves — the operator neither sold nor bills them.
        /// The nights stay part of the package in every other respect: the schedule, the departure
        /// date, Block Services and the expense cost all read <see cref="Nights"/> unchanged. This
        /// only says we do not charge for them, and records why.
        /// </summary>
        public bool ExtraNightsArrangedByGuest { get; set; }

        /// <summary>Chargeable nights on this row — derived from the schedule, never stored.</summary>
        public int ExtraNights => Math.Max(0, Nights - BaselineNights);

        /// <summary>What one extra night costs across every room kept — Σ(rooms × rate).</summary>
        public decimal ExtraNightPerNight => Rates.Sum(r => r.Quantity * r.Rate);

        /// <summary>
        /// False while this row has chargeable extra nights nobody has entered rates for. Feeds
        /// <see cref="Booking.IsPriced"/>, so an unpriced row makes the whole total read "—" instead
        /// of silently contributing 0.
        /// </summary>
        public bool ExtraNightsArePriced =>
            ExtraNights == 0 || ExtraNightsArrangedByGuest || Rates.Any(r => r.Quantity > 0);

        public decimal ExtraNightsTotal =>
            ExtraNightsArrangedByGuest ? 0m : ExtraNights * ExtraNightPerNight;

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
