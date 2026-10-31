using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
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
        private readonly RevenueReportExcelExporter _revenueExporter;
        private readonly ILogger<ReportController> _logger;

        public ReportController(
            IUnitOfWork unitOfWork,
            FlightReportExcelExporter exporter,
            RevenueReportExcelExporter revenueExporter,
            ILogger<ReportController> logger)
        {
            _unitOfWork = unitOfWork;
            _exporter = exporter;
            _revenueExporter = revenueExporter;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.Reports.ViewFlights)]
        public async Task<IActionResult> Flights(DateOnly? from, DateOnly? to, bool applied = false)
            => View(await BuildAsync(from, to, applied));

        [Authorize(Policy = Permissions.Reports.ExportFlights)]
        public async Task<IActionResult> ExportFlights(DateOnly? from, DateOnly? to, bool applied = false)
        {
            var model = await BuildAsync(from, to, applied);
            var bytes = _exporter.Build(model.Rows);
            _logger.LogInformation("Flights report exported: {From} to {To}, {Count} booking(s)", model.From, model.To, model.Rows.Count);

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

        // ─── Revenue ────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.Reports.ViewRevenue)]
        public async Task<IActionResult> Revenue(DateOnly? from, DateOnly? to, bool applied = false)
            => View(await BuildRevenueAsync(from, to, applied));

        [Authorize(Policy = Permissions.Reports.ExportRevenue)]
        public async Task<IActionResult> ExportRevenue(DateOnly? from, DateOnly? to, bool applied = false)
        {
            var model = await BuildRevenueAsync(from, to, applied);
            var bytes = _revenueExporter.Build(model);
            _logger.LogInformation("Revenue report exported: {From} to {To}, {Count} booking(s)", model.From, model.To, model.Rows.Count);

            return File(bytes, ExcelFormat.ContentType,
                        RevenueReportExcelExporter.FileName(model.From, model.To));
        }

        /// <summary>
        /// The single source of the revenue rows, so the table and the sheet can't disagree about what
        /// a trip earned. Two queries regardless of row count: the bookings, then every expense
        /// allocation belonging to them.
        /// </summary>
        private async Task<RevenueReportViewModel> BuildRevenueAsync(DateOnly? from, DateOnly? to, bool applied)
        {
            // Defaulted only on a first visit — the filter form posts applied=true, so a deliberately
            // cleared box stays cleared. A month back is a lower bound, so the UtcNow/local skew can
            // only widen the range.
            if (!applied && from is null && to is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

            var bookings = (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new RevenueReportSpec(from, to)))
                .ToList();

            var costs = await LoadCostsAsync(bookings.Select(b => b.Id).ToList());

            return new RevenueReportViewModel
            {
                From = from,
                To = to,
                Applied = applied,
                Rows = bookings.Select(b => BuildRevenueRow(b, costs.GetValueOrDefault(b.Id))).ToList(),
            };
        }

        /// <summary>
        /// Every expense allocated to these bookings, converted to the base currency.
        ///
        /// <para>
        /// One query per allocation table — <c>Booking</c> has no allocations navigation (the FK is
        /// NoAction with no inverse, per the cascade-path rule), so this reads the booking's shares of
        /// arrival-group expenses AND the expenses applied to it directly, and groups in memory. Each
        /// include chain reaches the paying safe's currency and the expense's stored rate.
        /// </para>
        ///
        /// <para>
        /// <b>Costs are not filtered by the report's date range.</b> The booking is the unit: a trip
        /// that ran in August whose hotel was settled in July still cost that money, and filtering by
        /// <c>SpentOn</c> would flatter August's margin while orphaning July's spend.
        /// </para>
        /// </summary>
        private async Task<Dictionary<int, BookingCost>> LoadCostsAsync(IReadOnlyList<int> bookingIds)
        {
            if (bookingIds.Count == 0)
                return new Dictionary<int, BookingCost>();

            var spec = new BaseSpecifications<BlockExpenseBooking>(
                a => a.BookingId != null && bookingIds.Contains(a.BookingId.Value));

            spec.AddInclude("ExpenseGroup.Expense.SafeEntry.Safe");

            var groupShares = await _unitOfWork.Repository<BlockExpenseBooking>().GetAllAsync(spec);

            var directSpec = new BaseSpecifications<BlockExpenseDirectBooking>(
                l => l.BookingId != null && bookingIds.Contains(l.BookingId.Value));

            directSpec.AddInclude("Expense.SafeEntry.Safe");

            var direct = await _unitOfWork.Repository<BlockExpenseDirectBooking>().GetAllAsync(directSpec);

            // Both kinds reduced to (booking, amount, the expense's rate) — one conversion rule for all.
            var lines = groupShares
                .Select(a => (BookingId: a.BookingId!.Value, a.Amount, Rate: a.ExpenseGroup?.Expense?.UnitsPerUsd))
                .Concat(direct.Select(l => (BookingId: l.BookingId!.Value, l.Amount, Rate: l.Expense?.UnitsPerUsd)));

            return lines
                .GroupBy(l => l.BookingId)
                .ToDictionary(g => g.Key, g =>
                {
                    var total = 0m;
                    var unrated = 0;

                    foreach (var line in g)
                    {
                        // No rate recorded means the cost can't be stated in the base currency. It is
                        // COUNTED and surfaced rather than folded in at face value — treating EGP as
                        // USD would understate the cost ~48-fold and flatter the margin.
                        if (line.Rate is { } rate && rate > 0m)
                            total += line.Amount / rate;
                        else
                            unrated++;
                    }

                    return new BookingCost(total, unrated);
                });
        }

        /// <param name="Amount">Allocated cost in the base currency, unrounded.</param>
        /// <param name="UnratedCount">Allocations left out for want of an exchange rate.</param>
        private sealed record BookingCost(decimal Amount, int UnratedCount);

        private static RevenueReportRow BuildRevenueRow(Booking booking, BookingCost? cost)
        {
            var expenses = cost?.Amount ?? 0m;
            var sale = booking.GrandTotal;

            // Null, not 0, while unpriced — "0.00 net" reads as break-even, the opposite of "unknown".
            // Same reasoning that keeps Booking.Balance nullable.
            var net = sale is { } total ? total - expenses : (decimal?)null;

            var margin = sale is { } s && s != 0m && net is { } n
                ? Math.Round(n / s * 100m, 1, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            return new RevenueReportRow(
                BookingId: booking.Id,
                BookingCode: booking.Code,
                LeadGuestName: booking.LeadGuestName,
                ArrivalDate: booking.ArrivalDate,
                PackageName: booking.Package?.Name ?? "—",
                Pax: booking.Pax,
                SalePrice: sale,
                ExpensesBase: expenses,
                UnratedExpenseCount: cost?.UnratedCount ?? 0,
                NetRevenue: net,
                MarginPercent: margin);
        }
    }
}
