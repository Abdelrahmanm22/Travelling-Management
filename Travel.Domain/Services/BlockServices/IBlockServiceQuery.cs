namespace Travel.Domain.Services.BlockServices
{
    /// <summary>
    /// Read-only projection over booking stays: bookings that check into the same destination on the
    /// same date are grouped into one "block file" the operator can send to the hotel or cruise line.
    ///
    /// A block only exists when <b>two or more distinct bookings</b> share the key — a lone booking is
    /// not a block and never appears. Nothing here is persisted; every call re-derives from live data.
    /// </summary>
    public interface IBlockServiceQuery
    {
        /// <summary>
        /// Blocks whose check-in falls inside the window, soonest first. Any filter may be null,
        /// in which case it is not applied.
        /// </summary>
        Task<IReadOnlyList<BlockSummary>> GetBlocksAsync(DateOnly? from, DateOnly? to, int? destinationId);

        /// <summary>
        /// One block file with its full booking + guest roster, or <c>null</c> when fewer than two
        /// bookings share the key (so a stale link 404s instead of rendering a one-booking "block").
        /// </summary>
        Task<BlockFile?> GetBlockAsync(int destinationId, DateOnly checkIn);
    }
}
