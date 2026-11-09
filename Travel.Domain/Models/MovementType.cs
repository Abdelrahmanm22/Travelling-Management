using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// A kind of internal movement the operator drags onto a booking: an airport pickup, a drop-off,
    /// a day tour. <see cref="FlightLeg"/> says which of the booking's flights it meets, or null when
    /// it isn't a flight. It is locked once any movement uses the type, because changing it would
    /// silently re-point every historical movement at a different flight.
    /// </summary>
    public class MovementType : BaseModel
    {
        public string Name { get; set; } = string.Empty;

        public FlightLeg? FlightLeg { get; set; }

        /// <summary>Inactive types leave the board's palette but still render on existing movements.</summary>
        public bool IsActive { get; set; } = true;
    }
}
