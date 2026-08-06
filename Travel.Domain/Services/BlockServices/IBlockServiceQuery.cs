using Travel.Domain.Enums;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>
    /// Read-only projection over booking stays: bookings that check into the same destination on the
    /// same date are grouped into one "block file" the operator can send to the hotel or cruise line.
    ///
    /// <b>Every</b> arrival gets a file, including a lone booking — the supplier needs the rooming list
    /// either way. The two-booking line only decides which of the two lists a group is shown in
    /// (Block Services vs Single Arrivals), not whether it exists. The groups themselves are still
    /// derived on every call; the only persisted fact about a group is its status, read here and
    /// written by <see cref="IBlockStatusService"/>.
    /// </summary>
    public interface IBlockServiceQuery
    {
        /// <summary>
        /// Arrival groups whose check-in falls inside the window, soonest first, split into blocks
        /// (two or more bookings) and single arrivals. Any filter may be null, in which case it is
        /// not applied.
        /// </summary>
        Task<ArrivalGroups> GetArrivalsAsync(DateOnly? from, DateOnly? to, int? destinationId, BlockStatus? status);

        /// <summary>
        /// One arrival file with its full booking + guest roster, or <c>null</c> when no stay matches
        /// the key at all (so a link to a destination + date that no longer has any booking 404s).
        /// A group holding a single booking is a valid file and renders normally.
        /// </summary>
        Task<BlockFile?> GetBlockAsync(int destinationId, DateOnly checkIn);
    }
}
