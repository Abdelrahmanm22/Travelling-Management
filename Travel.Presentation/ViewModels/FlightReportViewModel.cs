using System.ComponentModel.DataAnnotations;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Filter state plus results for Reports → Flights.</summary>
    public class FlightReportViewModel
    {
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        /// <summary>
        /// Posted as a hidden field by the filter form, so a deliberately cleared From box isn't
        /// silently refilled with today. Same idiom as the Block Services lists.
        /// </summary>
        public bool Applied { get; set; }

        public IReadOnlyList<FlightReportRow> Rows { get; set; } = new List<FlightReportRow>();

        /// <summary>True when at least one bound is set — distinguishes "no matches" from "no bookings yet".</summary>
        public bool HasFilters => From is not null || To is not null;

        /// <summary>One row is one booking, so the row count is the booking count.</summary>
        public int BookingCount => Rows.Count;

        public int PassengerCount => Rows.Sum(r => r.PaxNames.Count);
    }

    /// <summary>
    /// One booking's line. All of its guests live in <see cref="PaxNames"/> and render stacked inside a
    /// single cell, so the booking's flight facts are stated once rather than repeated per guest.
    /// Both the table and the .xlsx render this same record, so the screen and the sheet can't drift.
    /// </summary>
    public record FlightReportRow(
        string BookingCode,
        IReadOnlyList<string> PaxNames,
        DateOnly ArrivalDate,
        TimeOnly? ArrivalTime,
        string? ArrivalFlightNo,
        DateOnly DepartureDate,
        TimeOnly? DepartureTime,
        string? DepartureFlightNo);
}
