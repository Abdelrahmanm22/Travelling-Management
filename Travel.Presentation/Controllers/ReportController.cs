using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Specifications.Bookings;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// Operational reports built from live booking data — nothing here is stored, every request
    /// re-derives its rows.
    /// </summary>
    [Authorize]
    public class ReportController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly FlightReportExcelExporter _exporter;

        public ReportController(IUnitOfWork unitOfWork, FlightReportExcelExporter exporter)
        {
            _unitOfWork = unitOfWork;
            _exporter = exporter;
        }

        [Authorize(Policy = Permissions.Reports.ViewFlights)]
        public async Task<IActionResult> Flights(DateOnly? from, DateOnly? to, bool applied = false)
            => View(await BuildAsync(from, to, applied));

        [Authorize(Policy = Permissions.Reports.ExportFlights)]
        public async Task<IActionResult> ExportFlights(DateOnly? from, DateOnly? to, bool applied = false)
        {
            var model = await BuildAsync(from, to, applied);
            var bytes = _exporter.Build(model.Rows);

            return File(bytes, FlightReportExcelExporter.ContentType,
                        FlightReportExcelExporter.FileName(model.From, model.To));
        }

        /// <summary>
        /// The single source of the report's rows, so the table on screen and the downloaded sheet
        /// can never disagree about who is flying.
        /// </summary>
        private async Task<FlightReportViewModel> BuildAsync(DateOnly? from, DateOnly? to, bool applied)
        {
            // First visit defaults to upcoming movements. The filter form posts applied=true, so an
            // operator who deliberately clears the From box isn't silently pushed back to today.
            if (!applied && from is null && to is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow);

            var bookings = await _unitOfWork.Repository<Booking>().GetAllAsync(new FlightReportSpec(from, to));

            return new FlightReportViewModel
            {
                From = from,
                To = to,
                Applied = applied,
                Rows = BuildRows(bookings),
            };
        }

        /// <summary>
        /// One row per booking. Its guests are collected into a single list so they render stacked in
        /// one cell — the booking's flight facts are stated once, not repeated per passenger.
        /// A booking with no guests still gets a row (with an empty name list): dropping it would
        /// silently hide a real flight movement from the airport list.
        /// </summary>
        private static List<FlightReportRow> BuildRows(IEnumerable<Booking> bookings) =>
            bookings.Select(booking => new FlightReportRow(
                booking.Code,
                booking.Guests
                       .OrderByDescending(g => g.IsLeadGuest)
                       .ThenBy(g => g.SortOrder)
                       .Select(g => g.FullName)
                       .ToList(),
                // A null flight date means "not recorded yet" — the package date stands in, exactly as
                // the spec's filter does, so the date shown is always the date that matched.
                booking.FlightArrivalDate ?? booking.ArrivalDate,
                booking.ArrivalTime,
                booking.ArrivalFlightNo,
                booking.FlightDepartureDate ?? booking.DepartureDate,
                booking.DepartureTime,
                booking.DepartureFlightNo))
            .ToList();
    }
}
