using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Services.InternalMovements;
using Travel.Presentation.Helpers;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// The Movements report — the drivers' sheet. One day + one driver is tomorrow's sheet for that
    /// driver. Built once by <c>ReportController.BuildMovementsAsync</c>; the table and the .xlsx both
    /// render <see cref="Days"/>, so they can't drift.
    /// </summary>
    public class MovementReportViewModel
    {
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        [Display(Name = "Destination")]
        public int? DestinationId { get; set; }

        [Display(Name = "Driver")]
        public int? DriverId { get; set; }

        public bool Applied { get; set; }

        public IReadOnlyList<SelectListItem> Destinations { get; set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<SelectListItem> Drivers { get; set; } = Array.Empty<SelectListItem>();

        /// <summary>The chosen destination's name, or null for all of them.</summary>
        public string? DestinationName { get; set; }
        public string? DriverName { get; set; }

        public IReadOnlyList<MovementReportDay> Days { get; set; } = Array.Empty<MovementReportDay>();

        public bool HasFilters => From is not null || To is not null || DestinationId is not null || DriverId is not null;

        /// <summary>With no destination chosen, rows from several destinations can share a day — say which.</summary>
        public bool ShowDestination => DestinationId is null;

        public int MovementCount => Days.Sum(d => d.Rows.Count);
        public int NeedsReviewCount => Days.Sum(d => d.Rows.Count(r => r.NeedsReview));
        public int NoFlightCount => Days.Sum(d => d.Rows.Count(r => r.NoFlightRecorded));
    }

    public record MovementReportDay(DateOnly Date, IReadOnlyList<MovementReportRow> Rows);

    /// <summary>One movement as both renderers print it. Never carries a price or a phone number.</summary>
    public record MovementReportRow(
        int BookingId,
        string PickupTime,
        string BookingCode,
        string LeadGuest,
        int Pax,
        string Movement,
        string Destination,
        string Hotel,
        string FlightNo,
        string FlightTime,
        string Driver,
        string Status,
        bool NeedsReview,
        bool NoFlightRecorded)
    {
        public static MovementReportRow From(MovementLine line) => new(
            line.BookingId,
            MovementText.Time(line.PickupTime),
            line.BookingCode,
            line.LeadGuestName,
            line.Pax,
            line.MovementTypeName,
            line.DestinationName,
            line.Hotel ?? "—",
            MovementText.FlightNumber(line),
            MovementText.FlightTime(line),
            line.DriverName,
            MovementText.Status(line),
            line.NeedsReview,
            line.NoFlightRecorded);
    }
}
