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
        /// How many travellers are taking this tour — the count of <see cref="Guests"/> for every row
        /// saved since names were recorded. Stored rather than computed because tours sold before
        /// that carry a number with no names, and must keep their figure until next edited.
        /// </summary>
        public int Pax { get; set; } = 1;

        /// <summary>Who is taking it. Empty on a tour saved before names were recorded.</summary>
        public ICollection<BookingTourGuest> Guests { get; set; } = new List<BookingTourGuest>();

        public bool HasNamedGuests => Guests.Count > 0;

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
