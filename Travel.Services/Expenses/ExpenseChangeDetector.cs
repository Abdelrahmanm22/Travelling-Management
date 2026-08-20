using Travel.Domain;
using Travel.Domain.Services.Expenses;
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
        private readonly BlockGroupLoader _groups;

        public ExpenseChangeDetector(IUnitOfWork unitOfWork)
        {
            _groups = new BlockGroupLoader(unitOfWork);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<(int DestinationId, DateOnly CheckInDate), string>> CurrentFingerprintsAsync(
            IEnumerable<(int DestinationId, DateOnly CheckInDate)> keys)
        {
            var stays = await _groups.LoadGroupStaysAsync(keys);

            return stays.ToDictionary(
                entry => entry.Key,
                entry => ExpenseFingerprint.FromStays(entry.Value));
        }
    }
}
