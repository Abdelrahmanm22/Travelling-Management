using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications.BlockServices;

namespace Travel.Services.BlockServices
{
    /// <summary>
    /// Loads arrival groups by an arbitrary set of <see cref="BlockKey"/>s.
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
        public async Task<Dictionary<BlockKey, BlockServiceStatus>> LoadStatusesAsync(IEnumerable<BlockKey> keys)
        {
            var distinct = keys.ToHashSet();
            if (distinct.Count == 0) return new Dictionary<BlockKey, BlockServiceStatus>();

            var (destinationIds, from, to) = Window(distinct);

            var rows = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetAllAsync(new BlockStatusSpec(destinationIds, from, to));

            return rows
                .Where(r => distinct.Contains(KeyOf(r)))
                .ToDictionary(KeyOf);
        }

        /// <summary>
        /// The current membership of each group, de-duplicated to one stay per booking — what
        /// <c>BlockSnapshot.Resolve</c> compares a confirmation snapshot against. Carries only the
        /// rooming; cancelled bookings are excluded by <see cref="BlockStaysSpec"/> itself.
        /// </summary>
        public async Task<Dictionary<BlockKey, List<BookingStay>>> LoadGroupStaysAsync(IEnumerable<BlockKey> keys)
        {
            var distinct = keys.ToHashSet();
            if (distinct.Count == 0) return new Dictionary<BlockKey, List<BookingStay>>();

            var (destinationIds, from, to) = Window(distinct);

            var stays = await _unitOfWork.Repository<BookingStay>()
                .GetAllAsync(new BlockStaysSpec(destinationIds, from, to));

            return stays
                .Where(s => distinct.Contains(BlockKey.Of(s)))
                .GroupBy(BlockKey.Of)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(s => s.BookingId).Select(b => b.First()).ToList());
        }

        public static BlockKey KeyOf(BlockServiceStatus row) => new(row.DestinationId, row.CheckInDate, row.Category);

        // The category isn't narrowed in the database: there are only two, so it costs nothing to
        // match it in memory along with the exact date.
        private static (List<int> DestinationIds, DateOnly From, DateOnly To) Window(HashSet<BlockKey> keys) =>
            (keys.Select(k => k.DestinationId).Distinct().ToList(),
             keys.Min(k => k.CheckInDate),
             keys.Max(k => k.CheckInDate));
    }
}
