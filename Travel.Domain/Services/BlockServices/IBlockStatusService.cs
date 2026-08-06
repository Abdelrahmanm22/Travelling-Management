using Travel.Domain.Enums;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>What an operator posted from the block status modal.</summary>
    public sealed record BlockStatusChange(
        int DestinationId,
        DateOnly CheckInDate,
        BlockStatus Status,
        string? RequestEmailLink,
        string? ConfirmationEmailLink,
        string? UserId,
        string? UserName);

    /// <summary>
    /// The write side of Block Services, and the single owner of the rule linking the two statuses:
    /// a booking is <see cref="BookingStatus.Confirmed"/> exactly when every arrival group it belongs
    /// to is <see cref="BlockStatus.Confirmed"/>, and drops back to <see cref="BookingStatus.InProgress"/>
    /// the moment one of them isn't.
    ///
    /// <para>
    /// <see cref="BookingStatus.Completed"/> and <see cref="BookingStatus.Cancelled"/> are operator-owned
    /// and are never written by that recalculation.
    /// </para>
    /// </summary>
    public interface IBlockStatusService
    {
        /// <summary>The stored status of one group, or <see cref="BlockStatusInfo.NotBooked"/> if it has none.</summary>
        Task<BlockStatusInfo> GetAsync(int destinationId, DateOnly checkIn);

        /// <summary>
        /// Every arrival group a booking belongs to, in itinerary order, each with its block status —
        /// what <c>Booking/Details</c> shows so an operator can see why a booking isn't confirmed yet.
        /// </summary>
        Task<IReadOnlyList<BookingBlockLine>> GetForBookingAsync(int bookingId);

        /// <summary>
        /// Upserts a group's status and email links, then recalculates every booking in that group.
        /// Links are only ever written, never cleared — a blank field leaves the stored value alone.
        /// </summary>
        Task<OperationResult> SetStatusAsync(BlockStatusChange change);

        /// <summary>
        /// Re-derives <c>Booking.Status</c> from the statuses of the blocks each booking belongs to.
        /// Call after anything that changes a booking's block membership (create, edit, regenerate)
        /// as well as after a block status change. Does not call <c>CompleteAsync</c> — the caller
        /// saves, so this can join an existing unit of work.
        /// </summary>
        Task RecalculateAsync(IEnumerable<int> bookingIds);
    }
}
