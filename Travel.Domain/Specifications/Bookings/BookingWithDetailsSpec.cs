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

        // Booking.GrandTotal / PaidTotal are computed over Stays, Tours and Transactions, so a missing
        // include here doesn't error — it silently makes the total too small everywhere this spec is
        // used, which is the list, Details and the Excel export. Same trap as Nationality rendering "—".
        private void AddIncludes()
        {
            AddInclude("Package");
            AddInclude("Nationality");
            AddInclude("Stays.Destination");
            AddInclude("Guests");
            AddInclude("Rooms");
            AddInclude("Attachments");
            AddInclude("Tours.Tour");
            // "Transactions.Attachments", not just "Transactions": without the second level the
            // receipts navigation comes back empty and Booking/Details renders "—" for a payment that
            // does have a receipt — no error, and the global /Transaction list still shows it correctly
            // (TransactionSpec includes them), which makes the two pages disagree.
            AddInclude("Transactions.Attachments");
        }
    }
}
