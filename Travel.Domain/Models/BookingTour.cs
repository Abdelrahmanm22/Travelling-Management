namespace Travel.Domain.Models
{
    /// <summary>One excursion sold on a <see cref="Booking"/>.</summary>
    public class BookingTour : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int TourId { get; set; }
        public Tour Tour { get; set; } = null!;

        /// <summary>
        /// How many travellers are taking this tour — its own figure, not <see cref="Booking.Pax"/>.
        /// It defaults to the booking's PAX in the form, but not everyone takes every tour.
        /// </summary>
        public int Pax { get; set; } = 1;

        /// <summary>
        /// Per-person rate this booking was sold at — a snapshot copied from
        /// <see cref="Tour.PricePerPerson"/> when the row is added, then freely editable. Reading the
        /// price through the FK instead would let a catalogue edit silently restate old bookings.
        /// </summary>
        public decimal UnitPrice { get; set; }

        public int SortOrder { get; set; }

        public string? Notes { get; set; }

        public decimal Total => Pax * UnitPrice;
    }
}
