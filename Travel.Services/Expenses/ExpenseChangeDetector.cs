using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.Expenses;
using Travel.Domain.Specifications.Expenses;
using Travel.Services.BlockServices;

namespace Travel.Services.Expenses
{
    /// <inheritdoc cref="IExpenseChangeDetector" />
    /// <remarks>
    /// Reuses <see cref="BlockGroupLoader"/> rather than querying itself: that loader already runs one
    /// query for an arbitrary set of composite keys (EF can't translate a composite <c>Contains</c>),
    /// de-duplicates to one stay per booking, excludes cancelled bookings, and includes the rooming —
    /// which is exactly and only what the fingerprint reads.
    ///
    /// <para>
    /// ⚠️ <see cref="BlockGroupLoader"/> is <c>internal</c> to Travel.Services. That is fine here (same
    /// assembly) but it means this class cannot move to Travel.Presentation without widening the
    /// loader's accessibility first.
    /// </para>
    /// </remarks>
    public class ExpenseChangeDetector : IExpenseChangeDetector
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockGroupLoader _groups;

        public ExpenseChangeDetector(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _groups = new BlockGroupLoader(unitOfWork);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<BlockKey, string>> CurrentFingerprintsAsync(IEnumerable<BlockKey> keys)
        {
            var stays = await _groups.LoadGroupStaysAsync(keys);

            return stays.ToDictionary(
                entry => entry.Key,
                entry => ExpenseFingerprint.FromStays(entry.Value));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<int, string>> CurrentBookingFingerprintsAsync(IEnumerable<int> bookingIds)
        {
            var ids = bookingIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, string>();

            // The same spec the calculator charges from, so "what we paid for" and "what is there now"
            // are read through identical includes. No status filter: cancelling a booking changes none
            // of the facts a booking expense was split on.
            var bookings = await _unitOfWork.Repository<Booking>().GetAllAsync(new BookingsForExpenseSpec(ids));

            return bookings.ToDictionary(b => b.Id, ExpenseFingerprint.ForBooking);
        }
    }
}
