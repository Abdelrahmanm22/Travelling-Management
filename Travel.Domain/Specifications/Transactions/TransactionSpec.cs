using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Transactions
{
    /// <summary>
    /// The global ledger view. Every filter is optional; a null one means "no constraint", the same
    /// shape BookingWithDetailsSpec and FlightReportSpec use.
    /// </summary>
    public class TransactionSpec : BaseSpecifications<BookingTransaction>
    {
        public TransactionSpec(DateOnly? from, DateOnly? to, int? bookingId, PaymentMethod? method, TransactionType? type)
            : base(t => (from == null || t.PaidOn >= from)
                     && (to == null || t.PaidOn <= to)
                     && (bookingId == null || t.BookingId == bookingId)
                     && (method == null || t.Method == method)
                     && (type == null || t.Type == type))
        {
            AddInclude("Booking");
            AddInclude("Attachments");
            SetOrderByDesc(t => t.PaidOn);
        }

        /// <summary>One entry with everything the edit form and the delete path need.</summary>
        public TransactionSpec(int id) : base(t => t.Id == id)
        {
            AddInclude("Booking");
            AddInclude("Attachments");
        }

        /// <summary>Every entry on one booking, oldest first — the Details ledger.</summary>
        public TransactionSpec(int bookingId, bool forBooking) : base(t => t.BookingId == bookingId)
        {
            AddInclude("Attachments");
            SetOrderBy(t => t.PaidOn);
        }
    }
}
