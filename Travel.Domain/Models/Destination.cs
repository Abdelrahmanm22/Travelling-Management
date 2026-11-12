using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    public class Destination : BaseModel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>True for a cruise / sailing stay — its <see cref="DepartureDays"/> drive booking embarkation.</summary>
        public bool IsCruise { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Guests usually fly in here domestically. Only a default: the generator copies it onto every
        /// stay at this destination except a booking's first (the international arrival), as
        /// <see cref="BookingStay.RequiresDomesticFlight"/>, which the operator can then flip per stay.
        /// </summary>
        public bool UsuallyReachedByDomesticFlight { get; set; }

        /// <summary>
        /// Drivers are dispatched for guests here (airport pickups, day tours), so the destination gets
        /// an Internal Movements calendar and day board.
        /// </summary>
        public bool HasInternalMovements { get; set; }

        /// <summary>Sailing days — set only when <see cref="IsCruise"/> is true; null otherwise.</summary>
        public WeekDays? DepartureDays { get; set; }

        /// <summary>
        /// The contract's Hospedagem line, e.g. "Hotel {Category} com café da manhã" — {Category} becomes
        /// "4 estrelas" / "5 estrelas". Null → the destination isn't listed under Hospedagem.
        /// </summary>
        public string? ContractAccommodationText { get; set; }

        /// <summary>Title of a free day inserted for an extra night here, e.g. "Hurghada – dia livre".</summary>
        public string? ContractFreeDayTitle { get; set; }

        /// <summary>Body of that free day, e.g. "Café da manhã e dia livre no resort."</summary>
        public string? ContractFreeDayText { get; set; }

        public ICollection<PackageSegment> Segments { get; set; } = new List<PackageSegment>();
    }
}
