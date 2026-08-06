using Travel.Domain.Models;

namespace Travel.Domain.Specifications.BlockServices
{
    /// <summary>
    /// Loads stored arrival-group statuses. There is no composite-key <c>Contains</c> EF can translate,
    /// so the multi-key overload narrows to the destinations and date span actually in play and the
    /// exact (destination, check-in) match is done in memory — one query instead of one per group.
    /// </summary>
    public class BlockStatusSpec : BaseSpecifications<BlockServiceStatus>
    {
        // One group.
        public BlockStatusSpec(int destinationId, DateOnly checkIn)
            : base(x => x.DestinationId == destinationId && x.CheckInDate == checkIn)
        {
        }

        // Everything that could match a list page's filter window.
        public BlockStatusSpec(DateOnly? from, DateOnly? to, int? destinationId)
            : base(x => (from == null || x.CheckInDate >= from)
                     && (to == null || x.CheckInDate <= to)
                     && (destinationId == null || x.DestinationId == destinationId))
        {
        }

        // A superset of an arbitrary key set, narrowed afterwards by the caller.
        public BlockStatusSpec(IReadOnlyCollection<int> destinationIds, DateOnly from, DateOnly to)
            : base(x => destinationIds.Contains(x.DestinationId)
                     && x.CheckInDate >= from
                     && x.CheckInDate <= to)
        {
        }
    }
}
