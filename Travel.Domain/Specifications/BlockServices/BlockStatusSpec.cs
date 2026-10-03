using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.BlockServices
{
    /// <summary>
    /// Loads arrival-group records — status, email links, domestic flight documents and the record of
    /// what the supplier was told. There is no composite-key <c>Contains</c> EF can translate, so the
    /// multi-key overload narrows to the destinations and date span actually in play and the exact
    /// (destination, check-in, category) match is done in memory — one query instead of one per group.
    /// <para>
    /// Both collections are included on every overload deliberately: every caller needs the documents
    /// (for the missing-domestic-flight flag) or the record (to resolve
    /// <see cref="Enums.BlockStatus.NeedsAmendment"/>), and loading them here keeps those facts free
    /// on a query the read paths already run.
    /// </para>
    /// </summary>
    public class BlockStatusSpec : BaseSpecifications<BlockServiceStatus>
    {
        // One group.
        public BlockStatusSpec(int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId)
            : base(x => x.DestinationId == destinationId
                     && x.CheckInDate == checkIn
                     && x.Category == category
                     && x.PrivateBookingId == privateBookingId)
        {
            AddIncludes();
        }

        // Everything that could match a list page's filter window.
        public BlockStatusSpec(DateOnly? from, DateOnly? to, int? destinationId)
            : base(x => (from == null || x.CheckInDate >= from)
                     && (to == null || x.CheckInDate <= to)
                     && (destinationId == null || x.DestinationId == destinationId))
        {
            AddIncludes();
        }

        // A superset of an arbitrary key set, narrowed afterwards by the caller.
        public BlockStatusSpec(IReadOnlyCollection<int> destinationIds, DateOnly from, DateOnly to)
            : base(x => destinationIds.Contains(x.DestinationId)
                     && x.CheckInDate >= from
                     && x.CheckInDate <= to)
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("Attachments");
            AddInclude("RecordedBookings");
        }
    }
}
