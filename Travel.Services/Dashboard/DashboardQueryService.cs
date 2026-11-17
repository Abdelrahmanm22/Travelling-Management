using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.Dashboard;
using Travel.Domain.Services.Reports;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Bookings;
using Travel.Domain.Specifications.Dashboard;
using Travel.Domain.Specifications.Expenses;

namespace Travel.Services.Dashboard
{
    /// <inheritdoc cref="IDashboardQuery" />
    /// <remarks>
    /// Every section runs a fixed number of queries, never one per row, and reuses the owner of each
    /// rule rather than restating it: <see cref="IBlockServiceQuery"/> for groups and their resolved
    /// status, <see cref="RevenueReportSpec"/> + <see cref="IBookingCostService"/> +
    /// <see cref="RevenueFigures"/> for sales (so the Sales tiles equal the Revenue report's footer for
    /// the same range), and the <c>SignedAmount</c> properties for both ledgers.
    /// </remarks>
    public class DashboardQueryService : IDashboardQuery
    {
        /// <summary>How far ahead the arrivals chart looks, counting today.</summary>
        public const int ArrivalsChartDays = 30;

        /// <summary>How far ahead the attention lists look, counting today.</summary>
        public const int AttentionDays = 14;

        /// <summary>The monthly charts cover this many calendar months, ending with the current one.</summary>
        public const int ChartMonths = 12;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IBlockServiceQuery _blocks;
        private readonly IBookingCostService _costs;

        public DashboardQueryService(IUnitOfWork unitOfWork, IBlockServiceQuery blocks, IBookingCostService costs)
        {
            _unitOfWork = unitOfWork;
            _blocks = blocks;
            _costs = costs;
        }

        // ─── Operations ─────────────────────────────────────────────────────

        public async Task<OperationsStats> GetOperationsAsync(DateOnly today, DateOnly from, DateOnly to)
        {
            var chartEnd = today.AddDays(ArrivalsChartDays - 1);
            var attentionEnd = today.AddDays(AttentionDays - 1);

            // One window covers the live tiles, the 30-day chart and the attention window.
            var onTheGround = (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new DashboardOperationsBookingsSpec(today, chartEnd)))
                .ToList();

            static DateOnly Arrives(Booking b) => b.FlightArrivalDate ?? b.ArrivalDate;
            static DateOnly Departs(Booking b) => b.FlightDepartureDate ?? b.DepartureDate;
            static BookingCount Count(IEnumerable<Booking> bookings)
            {
                var list = bookings.ToList();
                return new BookingCount(list.Count, list.Sum(b => b.Pax));
            }

            var arrivalsByDay = Enumerable.Range(0, ArrivalsChartDays)
                .Select(i => today.AddDays(i))
                .Select(day =>
                {
                    var arriving = onTheGround.Where(b => Arrives(b) == day).ToList();
                    return new DailyArrivals(day, arriving.Count, arriving.Sum(b => b.Pax));
                })
                .ToList();

            var missingArrivalFlights = onTheGround
                .Where(b => Arrives(b) >= today && Arrives(b) <= attentionEnd
                         && string.IsNullOrWhiteSpace(b.ArrivalFlightNo))
                .OrderBy(Arrives).ThenBy(b => b.Code)
                .Select(b => new MissingArrivalFlight(b.Id, b.Code, b.LeadGuestName, Arrives(b), b.Pax))
                .ToList();

            // Every booking arriving in the period, any status: the donut shows cancellations, the
            // period count doesn't.
            var periodBookings = (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new DashboardBookingStatusSpec(from, to)))
                .ToList();

            var bookingStatuses = periodBookings
                .GroupBy(b => b.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            // Groups and their resolved status come from Block Services itself — Blocks and Singles
            // together, because every arrival is a file the supplier needs.
            var periodGroups = Flatten(await _blocks.GetArrivalsAsync(from, to, null, null));

            var blockStatuses = periodGroups
                .GroupBy(g => g.Status.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            var attentionGroups = Flatten(await _blocks.GetArrivalsAsync(today, attentionEnd, null, null));

            return new OperationsStats(
                ArrivingToday: Count(onTheGround.Where(b => Arrives(b) == today)),
                DepartingToday: Count(onTheGround.Where(b => Departs(b) == today)),
                InCountry: Count(onTheGround.Where(b => Arrives(b) <= today && Departs(b) >= today)),
                ArrivingInPeriod: Count(periodBookings.Where(b => b.Status != BookingStatus.Cancelled)),
                BlockStatuses: blockStatuses,
                BlockGroupCount: periodGroups.Count,
                ArrivalsByDay: arrivalsByDay,
                BookingStatuses: bookingStatuses,
                AttentionFrom: today,
                AttentionTo: attentionEnd,
                BlocksNeedingAction: attentionGroups
                    .Where(g => g.Status.Status is BlockStatus.NotBooked or BlockStatus.NeedsAmendment)
                    .ToList(),
                MissingArrivalFlights: missingArrivalFlights,
                MissingDomesticFlights: attentionGroups
                    .Where(g => g.MissingDomesticFlightCount > 0)
                    .ToList());
        }

        private static List<BlockSummary> Flatten(ArrivalGroups groups) =>
            groups.Blocks.Concat(groups.Singles)
                .OrderBy(g => g.CheckInDate)
                .ThenBy(g => g.DestinationName)
                .ThenBy(g => g.Category)
                .ToList();

        // ─── Sales & revenue ────────────────────────────────────────────────

        public async Task<SalesStats> GetSalesAsync(DateOnly today, DateOnly from, DateOnly to)
        {
            var (chartStart, chartEnd) = ChartWindow(today);

            // One load over the union of the period and the chart window, split in memory — the same
            // spec the Revenue report uses, so the same bookings qualify.
            var loadFrom = from < chartStart ? from : chartStart;
            var loadTo = to > chartEnd ? to : chartEnd;

            var bookings = (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new RevenueReportSpec(loadFrom, loadTo)))
                .ToList();

            var costs = await _costs.LoadCostsAsync(bookings.Select(b => b.Id).ToList());

            SalesFigures Figures(IEnumerable<Booking> rows)
            {
                var list = rows.ToList();

                // The Revenue report's footer, term for term: sale over priced rows only, expenses
                // over every row (they were really paid), net and margin from those two.
                var sale = list.Sum(b => b.GrandTotal ?? 0m);
                var expenses = list.Sum(b => costs.GetValueOrDefault(b.Id)?.AmountUsd ?? 0m);
                var net = sale - expenses;

                return new SalesFigures(
                    BookingCount: list.Count,
                    Sale: sale,
                    Expenses: expenses,
                    Net: net,
                    MarginPercent: RevenueFigures.MarginPercent(sale, net),
                    UnpricedCount: list.Count(b => !b.IsPriced),
                    UnratedExpenseCount: list.Sum(b => costs.GetValueOrDefault(b.Id)?.UnratedCount ?? 0));
            }

            var months = Months(chartStart)
                .Select(m => new MonthlySales(m.Year, m.Month,
                    Figures(bookings.Where(b => b.ArrivalDate.Year == m.Year && b.ArrivalDate.Month == m.Month))))
                .ToList();

            return new SalesStats(
                Period: Figures(bookings.Where(b => b.ArrivalDate >= from && b.ArrivalDate <= to)),
                Months: months);
        }

        // ─── Payments ───────────────────────────────────────────────────────

        public async Task<PaymentsStats> GetPaymentsAsync(DateOnly today, DateOnly from, DateOnly to)
        {
            var (chartStart, chartEnd) = ChartWindow(today);
            var loadFrom = from < chartStart ? from : chartStart;
            var loadTo = to > chartEnd ? to : chartEnd;

            var transactions = (await _unitOfWork.Repository<BookingTransaction>()
                .GetAllAsync(new DashboardTransactionsSpec(loadFrom, loadTo)))
                .ToList();

            var inPeriod = transactions.Where(t => t.PaidOn >= from && t.PaidOn <= to).ToList();
            var payments = inPeriod.Where(t => t.Type != TransactionType.Refund).ToList();
            var refunds = inPeriod.Where(t => t.Type == TransactionType.Refund).ToList();

            var receivedByMonth = Months(chartStart)
                .Select(m => new MonthlyAmount(m.Year, m.Month, transactions
                    .Where(t => t.PaidOn.Year == m.Year && t.PaidOn.Month == m.Month)
                    .Sum(t => t.SignedAmount)))
                .ToList();

            var bookings = (await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new DashboardBalanceSpec()))
                .ToList();

            // Balance is null while unpriced, so those can't be "owed" — they are counted separately.
            var owing = bookings.Where(b => b.Balance is > 0m).ToList();

            return new PaymentsStats(
                PaymentsTotal: payments.Sum(t => t.Amount),
                PaymentCount: payments.Count,
                RefundsTotal: refunds.Sum(t => t.Amount),
                RefundCount: refunds.Count,
                Outstanding: owing.Sum(b => b.Balance!.Value),
                OutstandingCount: owing.Count,
                UnpricedCount: bookings.Count(b => !b.IsPriced),
                ReceivedByMonth: receivedByMonth);
        }

        // ─── Money safes & expenses ─────────────────────────────────────────

        public async Task<SafesStats> GetSafesAsync(DateOnly from, DateOnly to)
        {
            var safesSpec = new BaseSpecifications<MoneySafe>();
            safesSpec.SetOrderBy(s => s.Name);

            var safes = await _unitOfWork.Repository<MoneySafe>().GetAllAsync(safesSpec);

            // MoneySafeController.Index's shape: every entry once, grouped in memory.
            var balances = (await _unitOfWork.Repository<SafeEntry>().GetAllAsync(new SafeBalanceSpec()))
                .GroupBy(e => e.SafeId)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.SignedAmount));

            var safeLines = safes
                .Select(s => new SafeBalance(s.Id, s.Name, s.Currency, balances.GetValueOrDefault(s.Id), s.IsActive))
                // A retired safe still holding money is still money.
                .Where(s => s.IsActive || s.Balance != 0m)
                .ToList();

            var expenses = await _unitOfWork.Repository<BlockExpense>()
                .GetAllAsync(new DashboardExpensesSpec(from, to));

            var byCurrency = expenses
                .GroupBy(e => e.SafeEntry?.Safe?.Currency ?? string.Empty)
                .Select(g => new CurrencyTotal(g.Key, g.Sum(e => e.TotalAmount), g.Count()))
                .OrderBy(c => c.Currency)
                .ToList();

            return new SafesStats(safeLines, byCurrency);
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        /// <summary>The first day of the month <see cref="ChartMonths"/> − 1 back, to the end of this month.</summary>
        private static (DateOnly Start, DateOnly End) ChartWindow(DateOnly today)
        {
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            return (thisMonth.AddMonths(-(ChartMonths - 1)), thisMonth.AddMonths(1).AddDays(-1));
        }

        private static IEnumerable<DateOnly> Months(DateOnly start) =>
            Enumerable.Range(0, ChartMonths).Select(start.AddMonths);
    }
}
