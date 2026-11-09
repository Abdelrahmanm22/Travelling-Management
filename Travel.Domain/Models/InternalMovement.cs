namespace Travel.Domain.Models
{
    /// <summary>
    /// One driver dispatch for one booking inside one destination on one day — an airport pickup, a
    /// drop-off, a day tour. Assigned by hand on the day board.
    ///
    /// <para>
    /// Nothing about the flight, the hotel or whether the movement still fits its booking is stored:
    /// all of it is derived at read time by <c>MovementPresence</c>, so a flight number corrected on the
    /// booking shows up here with no further action, and a booking that moves off this day flags the
    /// movement "Needs review" instead of silently losing it.
    /// </para>
    /// </summary>
    public class InternalMovement : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        /// <summary>The board day it was assigned on.</summary>
        public DateOnly Date { get; set; }

        public int MovementTypeId { get; set; }
        public MovementType MovementType { get; set; } = null!;

        public int DriverId { get; set; }
        public Driver Driver { get; set; } = null!;

        public TimeOnly PickupTime { get; set; }

        /// <summary>Defaults to the booking's pax, but not everyone takes every movement.</summary>
        public int Pax { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Name snapshots, no FK — same reasoning as <see cref="BlockFlight.CreatedByName"/>.</summary>
        public string? CreatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }
    }
}
