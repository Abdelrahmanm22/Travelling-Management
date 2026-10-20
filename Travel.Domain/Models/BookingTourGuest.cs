namespace Travel.Domain.Models
{
    /// <summary>One named guest taking one <see cref="BookingTour"/>.</summary>
    public class BookingTourGuest : BaseModel
    {
        public int BookingTourId { get; set; }
        public BookingTour BookingTour { get; set; } = null!;

        public int GuestId { get; set; }
        public BookingGuest Guest { get; set; } = null!;
    }
}
