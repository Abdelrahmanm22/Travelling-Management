using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Services.Itinerary
{
    /// <summary>
    /// Generates the night-by-night accommodation schedule for a booking from a package definition and a
    /// guest arrival date. Pure and unit-testable — no EF or web dependencies.
    /// </summary>
    public interface IItineraryGeneratorService
    {
        /// <param name="package">Loaded with BaseDestination and Segments.Destination.</param>
        /// <param name="arrival">Guest arrival date.</param>
        /// <param name="embarkPreference">Chosen cruise embark weekday when the arrival day allows two (Mon/Tue); else ignored.</param>
        /// <param name="departure">Required only for variable-length packages (TotalNights == null); ignored otherwise.</param>
        GeneratedItinerary Generate(Package package, DateOnly arrival, WeekDays? embarkPreference, DateOnly? departure);

        /// <summary>
        /// Sailings the guest could board for this arrival, built from the cruise destination's departure days
        /// and ordered soonest-first (first = default). Empty when the package has no cruise segment.
        /// </summary>
        IReadOnlyList<EmbarkOption> GetEmbarkOptions(Package package, DateOnly arrival);
    }
}
