namespace Travel.Domain.Models
{
    /// <summary>
    /// One domestic flight into an arrival group — the leg that brings guests to this destination on
    /// (or around) its check-in date. It carries the flight facts, one or more ticket files, and the
    /// bookings actually on it. A booking whose stay in this group needs a domestic flight
    /// (<see cref="BookingStay.RequiresDomesticFlight"/>) is covered only when a flight here ticks it.
    ///
    /// <para>
    /// The date is recorded as entered and never validated against the check-in: an overnight flight
    /// the evening before is routine. The Details page notes the difference inline instead.
    /// </para>
    /// </summary>
    public class BlockFlight : BaseModel
    {
        public int BlockServiceStatusId { get; set; }
        public BlockServiceStatus BlockServiceStatus { get; set; } = null!;

        public string FlightNumber { get; set; } = string.Empty;
        public DateOnly FlightDate { get; set; }
        public TimeOnly FlightTime { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Name snapshots, no FK — same reasoning as <see cref="BlockServiceStatus.UpdatedByName"/>.</summary>
        public string? CreatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }

        public ICollection<BlockAttachment> Files { get; set; } = new List<BlockAttachment>();
        public ICollection<BlockFlightBooking> Bookings { get; set; } = new List<BlockFlightBooking>();
    }
}
