using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// The form asks "Is it a flight?", then International / Domestic, then Arrival / Departure, and the
    /// controller composes the answers into one <see cref="Domain.Enums.FlightLeg"/>.
    /// </summary>
    public class MovementTypeViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        [Remote(action: "IsNameAvailable", controller: "MovementType", AdditionalFields = nameof(Id),
            ErrorMessage = "A movement type with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Is it a flight?")]
        public bool IsFlight { get; set; }

        [Display(Name = "Flight")]
        public FlightScope? Scope { get; set; }

        [Display(Name = "Direction")]
        public FlightDirection? Direction { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        /// <summary>Set by the controller: a type any movement uses has its flight answers locked.</summary>
        public bool IsInUse { get; set; }

        public FlightLeg? ToLeg() => !IsFlight || Scope is null || Direction is null
            ? null
            : (Scope, Direction) switch
            {
                (FlightScope.International, FlightDirection.Arrival) => FlightLeg.InternationalArrival,
                (FlightScope.International, FlightDirection.Departure) => FlightLeg.InternationalDeparture,
                (FlightScope.Domestic, FlightDirection.Arrival) => FlightLeg.DomesticArrival,
                _ => FlightLeg.DomesticDeparture,
            };

        public void SetLeg(FlightLeg? leg)
        {
            IsFlight = leg is not null;
            Scope = leg is null ? null : leg.Value.IsDomestic() ? FlightScope.Domestic : FlightScope.International;
            Direction = leg is null ? null : leg.Value.IsArrival() ? FlightDirection.Arrival : FlightDirection.Departure;
        }

        public enum FlightScope
        {
            International = 1,
            Domestic = 2,
        }

        public enum FlightDirection
        {
            Arrival = 1,
            Departure = 2,
        }
    }
}
