using System.ComponentModel.DataAnnotations;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Filter state plus results for Reports → Tours.</summary>
    public class ToursReportViewModel
    {
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        /// <summary>Posted as a hidden field by the filter form — the same idiom as the other reports.</summary>
        public bool Applied { get; set; }

        /// <summary>One column per tour sold on the listed bookings, ordered by name.</summary>
        public IReadOnlyList<ToursReportColumn> Tours { get; set; } = new List<ToursReportColumn>();

        public IReadOnlyList<ToursReportBooking> Bookings { get; set; } = new List<ToursReportBooking>();

        /// <summary>Tours sold before guest names were recorded — a pax count with nobody ticked.</summary>
        public int UnrecordedTourCount { get; set; }

        public bool HasFilters => From is not null || To is not null;

        public int BookingCount => Bookings.Count;

        public int GuestCount => Bookings.Sum(b => b.Guests.Count);
    }

    public record ToursReportColumn(int TourId, string Name);

    /// <summary>
    /// One booking's block of guest rows. Both the table and the .xlsx render this same record, so the
    /// screen and the sheet can't drift.
    /// </summary>
    /// <param name="Guests">Never empty — a booking without guests carries one "—" row, so its tours still show.</param>
    /// <param name="UnnamedPax">Tour id → pax on tours of that kind saved with no names.</param>
    public record ToursReportBooking(
        string BookingCode,
        DateOnly ArrivalDate,
        DateOnly DepartureDate,
        string PackageName,
        IReadOnlyList<ToursReportGuest> Guests,
        IReadOnlyDictionary<int, int> UnnamedPax);

    /// <param name="TourIds">The tours this guest is ticked on.</param>
    public record ToursReportGuest(string Name, IReadOnlySet<int> TourIds);
}
