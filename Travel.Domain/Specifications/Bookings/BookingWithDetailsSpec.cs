using Travel.Domain.Enums;
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

        // The filtered list — any of the four may be null, meaning "no constraint".
        public BookingWithDetailsSpec(DateOnly? arrivalFrom, DateOnly? arrivalTo, int? packageId, BookingStatus? status)
            : base(b => (arrivalFrom == null || b.ArrivalDate >= arrivalFrom)
                     && (arrivalTo == null || b.ArrivalDate <= arrivalTo)
                     && (packageId == null || b.PackageId == packageId)
                     && (status == null || b.Status == status))
        {
            AddIncludes();
            SetOrderByDesc(b => b.ArrivalDate);
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
