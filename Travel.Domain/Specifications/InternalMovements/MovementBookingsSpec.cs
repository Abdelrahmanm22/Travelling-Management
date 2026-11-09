using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.InternalMovements
{
    /// <summary>
    /// The candidate bookings for an Internal Movements window: every live booking whose trip — package
    /// dates or flight dates, whichever is wider — touches it. Deliberately a superset; the exact
    /// "is it in this destination on this day" rule runs in memory in <c>MovementPresence</c>, since it
    /// reads stays, flights and domestic flights together.
    /// </summary>
    public class MovementBookingsSpec : BaseSpecifications<Booking>
    {
        public MovementBookingsSpec(DateOnly from, DateOnly to)
            : base(b => b.Status != BookingStatus.Cancelled
                     && (b.ArrivalDate <= to || (b.FlightArrivalDate != null && b.FlightArrivalDate <= to))
                     && (b.DepartureDate >= from || (b.FlightDepartureDate != null && b.FlightDepartureDate >= from)))
        {
            AddIncludes();
        }

        /// <summary>Specific bookings, for validating a save. Cancelled ones are loaded so they can be refused by name.</summary>
        public MovementBookingsSpec(IReadOnlyCollection<int> bookingIds)
            : base(b => bookingIds.Contains(b.Id))
        {
            AddIncludes();
        }

        // Stays + Guests: two sibling collections that grow with the business — split.
        private void AddIncludes()
        {
            AddInclude("Stays.Destination");
            AddInclude("Guests");
            UseSplitQuery();
        }
    }
}
