using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    public class BookingWithDetailsSpec : BaseSpecifications<Booking>
    {
        // All bookings, newest arrival first.
        public BookingWithDetailsSpec()
        {
            AddIncludes();
            SetOrderByDesc(b => b.ArrivalDate);
        }

        // A single booking by id.
        public BookingWithDetailsSpec(int id) : base(b => b.Id == id)
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("Package");
            AddInclude("Stays.Destination");
            AddInclude("Guests");
            AddInclude("Rooms");
            AddInclude("Attachments");
        }
    }
}
