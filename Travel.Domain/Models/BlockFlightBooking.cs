namespace Travel.Domain.Models
{
    /// <summary>
    /// A booking ticked on a <see cref="BlockFlight"/>. A link to a booking that has since left the
    /// group (dates edited, cancelled, moved category) is kept: it is harmless, the edit form cannot
    /// show it, and it re-applies if the booking returns.
    /// </summary>
    public class BlockFlightBooking : BaseModel
    {
        public int BlockFlightId { get; set; }
        public BlockFlight BlockFlight { get; set; } = null!;

        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;
    }
}
