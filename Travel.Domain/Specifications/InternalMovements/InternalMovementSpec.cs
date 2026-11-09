using Travel.Domain.Models;

namespace Travel.Domain.Specifications.InternalMovements
{
    /// <summary>
    /// Movements with everything needed to re-derive their flight, hotel and review state: the type,
    /// driver, destination and the booking with its stays and guests.
    /// </summary>
    public class InternalMovementSpec : BaseSpecifications<InternalMovement>
    {
        // One movement — criteria is the Id, so split + GetByIdAsync is safe.
        public InternalMovementSpec(int id)
            : base(m => m.Id == id)
        {
            AddIncludes();
        }

        // One destination over a date span (a board day, or a calendar month).
        public InternalMovementSpec(int destinationId, DateOnly from, DateOnly to)
            : base(m => m.DestinationId == destinationId && m.Date >= from && m.Date <= to)
        {
            AddIncludes();
        }

        // The report: optional destination and driver.
        public InternalMovementSpec(DateOnly? from, DateOnly? to, int? destinationId, int? driverId)
            : base(m => (from == null || m.Date >= from)
                     && (to == null || m.Date <= to)
                     && (destinationId == null || m.DestinationId == destinationId)
                     && (driverId == null || m.DriverId == driverId))
        {
            AddIncludes();
            SetOrderBy(m => m.Date);
        }

        private void AddIncludes()
        {
            AddInclude("MovementType");
            AddInclude("Driver");
            AddInclude("Destination");
            AddInclude("Booking.Stays.Destination");
            AddInclude("Booking.Guests");
            UseSplitQuery();
        }
    }
}
