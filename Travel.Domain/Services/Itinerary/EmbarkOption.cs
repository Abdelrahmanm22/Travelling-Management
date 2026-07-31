using Travel.Domain.Enums;

namespace Travel.Domain.Services.Itinerary
{
    /// <summary>
    /// One sailing the guest could board for a given arrival date, derived from the cruise
    /// <see cref="Models.Destination.DepartureDays"/>. The operator picks among these per booking.
    /// </summary>
    public class EmbarkOption
    {
        public WeekDays Day { get; set; }

        /// <summary>Actual embarkation date for this arrival.</summary>
        public DateOnly Date { get; set; }

        /// <summary>Nights spent at the base destination before leaving for the cruise.</summary>
        public int LeadNights { get; set; }

        /// <summary>Nights this choice would add beyond the package total (0 when it fits).</summary>
        public int ExtraNights { get; set; }
    }
}
