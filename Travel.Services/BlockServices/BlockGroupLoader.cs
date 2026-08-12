using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Specifications.BlockServices;

namespace Travel.Services.BlockServices
{
    /// <summary>
    /// Loads arrival groups by an arbitrary set of <c>(destination, check-in)</c> keys.
    ///
    /// <para>
    /// EF can't translate a composite-key <c>Contains</c>, so both methods narrow the query to the
    /// destinations in play and the min/max date span they cover, then do the exact key match in
    /// memory — one query instead of one per group. Shared by the read and write sides so that trick
    /// exists once.
    /// </para>
    /// </summary>
    internal sealed class BlockGroupLoader
    {
        private readonly IUnitOfWork _unitOfWork;

        public BlockGroupLoader(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <summary>The persisted record of each group that has one — status, links, documents, snapshot.</summary>
        public async Task<Dictionary<(int, DateOnly), BlockServiceStatus>> LoadStatusesAsync(
            IEnumerable<(int DestinationId, DateOnly CheckInDate)> keys)
        {
            var distinct = keys.Distinct().ToHashSet();
            if (distinct.Count == 0) return new Dictionary<(int, DateOnly), BlockServiceStatus>();

            var (destinationIds, from, to) = Window(distinct);

            var rows = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetAllAsync(new BlockStatusSpec(destinationIds, from, to));

            return rows
                .Where(r => distinct.Contains((r.DestinationId, r.CheckInDate)))
                .ToDictionary(r => (r.DestinationId, r.CheckInDate));
        }

        /// <summary>
        /// The current membership of each group, de-duplicated to one stay per booking — what
        /// <c>BlockConfirmation.Resolve</c> compares a confirmation snapshot against. Carries only the
        /// rooming; cancelled bookings are excluded by <see cref="BlockStaysSpec"/> itself.
        /// </summary>
        public async Task<Dictionary<(int, DateOnly), List<BookingStay>>> LoadGroupStaysAsync(
            IEnumerable<(int DestinationId, DateOnly CheckInDate)> keys)
        {
            var distinct = keys.Distinct().ToHashSet();
            if (distinct.Count == 0) return new Dictionary<(int, DateOnly), List<BookingStay>>();

            var (destinationIds, from, to) = Window(distinct);

            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(destinationIds, from, to));

            return stays
                .Where(s => distinct.Contains((s.DestinationId, s.CheckInDate)))
                .GroupBy(s => (s.DestinationId, s.CheckInDate))
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(s => s.BookingId).Select(b => b.First()).ToList());
        }

        private static (List<int> DestinationIds, DateOnly From, DateOnly To) Window(
            HashSet<(int DestinationId, DateOnly CheckInDate)> keys) =>
            (keys.Select(k => k.DestinationId).Distinct().ToList(),
             keys.Min(k => k.CheckInDate),
             keys.Max(k => k.CheckInDate));
    }
}
