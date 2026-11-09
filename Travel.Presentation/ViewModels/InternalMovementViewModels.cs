using Travel.Domain.Models;
using Travel.Domain.Services.InternalMovements;

namespace Travel.Presentation.ViewModels
{
    public class InternalMovementIndexViewModel
    {
        public IReadOnlyList<Destination> Destinations { get; init; } = Array.Empty<Destination>();
    }

    public class InternalMovementCalendarViewModel
    {
        public int DestinationId { get; init; }
        public string DestinationName { get; init; } = string.Empty;

        /// <summary>The first day of the month shown.</summary>
        public DateOnly Month { get; init; }
        public DateOnly Today { get; init; }
        public IReadOnlyList<CalendarDay> Days { get; init; } = Array.Empty<CalendarDay>();

        /// <summary>The other flagged destinations, for a switcher. Empty when this is the only one.</summary>
        public IReadOnlyList<Destination> OtherDestinations { get; init; } = Array.Empty<Destination>();

        public int NeedsReviewTotal => Days.Sum(d => d.NeedsReview);
    }

    public class InternalMovementDayViewModel
    {
        public DayBoard Board { get; init; } = new();

        /// <summary>Active types — the palette and the + button's select.</summary>
        public IReadOnlyList<MovementType> Types { get; init; } = Array.Empty<MovementType>();

        /// <summary>Active drivers only: an inactive driver can't be picked for a new movement.</summary>
        public IReadOnlyList<Driver> Drivers { get; init; } = Array.Empty<Driver>();

        public bool CanManage { get; init; }
        public bool CanOpenBooking { get; init; }
        public DateOnly Today { get; init; }
    }

    /// <summary>One booking card on the board, with the two permissions it renders differently for.</summary>
    public record BoardCardViewModel(BoardBooking Card, bool CanManage, bool CanOpenBooking, DateOnly Date);

    /// <summary>
    /// The popup's JSON post. Create (<see cref="Id"/> null) carries one or more bookings and makes one
    /// row each; edit carries the one booking and changes only type, time, driver and pax.
    /// </summary>
    public class MovementSaveRequest
    {
        public int? Id { get; set; }
        public int DestinationId { get; set; }
        public DateOnly Date { get; set; }
        public int MovementTypeId { get; set; }
        public int DriverId { get; set; }

        /// <summary>"HH:mm" from the time input; parsed server-side so a blank box is a readable error.</summary>
        public string? PickupTime { get; set; }

        public List<MovementBookingInput> Bookings { get; set; } = new();
    }

    public class MovementBookingInput
    {
        public int BookingId { get; set; }
        public int Pax { get; set; }
    }
}
