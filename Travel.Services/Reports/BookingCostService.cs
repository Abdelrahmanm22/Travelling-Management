using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Services.Reports;
using Travel.Domain.Specifications;

namespace Travel.Services.Reports
{
    /// <inheritdoc cref="IBookingCostService" />
    /// <remarks>
    /// One query per allocation table — <c>Booking</c> has no allocations navigation (the FK is
    /// NoAction with no inverse, per the cascade-path rule), so this reads the booking's shares of
    /// arrival-group expenses AND the expenses applied to it directly, and groups in memory. Each
    /// include chain reaches the expense's stored rate.
    /// </remarks>
    public class BookingCostService : IBookingCostService
    {
        private readonly IUnitOfWork _unitOfWork;

        public BookingCostService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public async Task<IReadOnlyDictionary<int, BookingCost>> LoadCostsAsync(IReadOnlyCollection<int> bookingIds)
        {
            if (bookingIds.Count == 0)
                return new Dictionary<int, BookingCost>();

            var ids = bookingIds.ToList();

            var spec = new BaseSpecifications<BlockExpenseBooking>(
                a => a.BookingId != null && ids.Contains(a.BookingId.Value));

            spec.AddInclude("ExpenseGroup.Expense.SafeEntry.Safe");

            var groupShares = await _unitOfWork.Repository<BlockExpenseBooking>().GetAllAsync(spec);

            var directSpec = new BaseSpecifications<BlockExpenseDirectBooking>(
                l => l.BookingId != null && ids.Contains(l.BookingId.Value));

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
                        // No rate recorded means the cost can't be stated in the base currency.
                        if (line.Rate is { } rate && rate > 0m)
                            total += line.Amount / rate;
                        else
                            unrated++;
                    }

                    return new BookingCost(total, unrated);
                });
        }
    }
}
