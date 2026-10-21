using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Expenses
{
    /// <summary>
    /// The booking picker on the expense form: arriving in a window, optionally visiting a destination
    /// on any stay, optionally matching a code or any guest's name. Cancelled bookings are left out
    /// unless asked for — they are listed only to record a real cost such as a cancellation fee.
    /// </summary>
    public class ExpensePickerBookingsSpec : BaseSpecifications<Booking>
    {
        public ExpensePickerBookingsSpec(
            DateOnly? from, DateOnly? to, int? destinationId, string? search, bool includeCancelled)
            : base(b => (!from.HasValue || b.ArrivalDate >= from.Value)
                     && (!to.HasValue || b.ArrivalDate <= to.Value)
                     && (!destinationId.HasValue || b.Stays.Any(s => s.DestinationId == destinationId.Value))
                     && (search == null || b.Code.Contains(search) || b.Guests.Any(g => g.FullName.Contains(search)))
                     && (includeCancelled || b.Status != BookingStatus.Cancelled))
        {
            AddIncludes();
            SetOrderBy(b => b.ArrivalDate);
        }

        /// <summary>Specific bookings — the selected ones the current filter doesn't show.</summary>
        public ExpensePickerBookingsSpec(IReadOnlyCollection<int> bookingIds)
            : base(b => bookingIds.Contains(b.Id))
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("Package");
            AddInclude("Stays.Destination");
            AddInclude("Rooms");
            AddInclude("Guests");
        }
    }
}
