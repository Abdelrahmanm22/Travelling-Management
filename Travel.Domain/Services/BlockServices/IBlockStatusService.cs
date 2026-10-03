using Travel.Domain.Enums;

namespace Travel.Domain.Services.BlockServices
{
    /// <summary>What an operator posted from the block status modal.</summary>
    public sealed record BlockStatusChange(
        BlockKey Key,
        BlockStatus Status,
        string? RequestEmailLink,
        string? ConfirmationEmailLink,
        string? UserId,
        string? UserName);

    /// <summary>
    /// The write side of Block Services, and the single owner of the rule linking the two statuses:
    /// a booking is <see cref="BookingStatus.Confirmed"/> exactly when every arrival group on its
    /// itinerary holds a confirmation snapshot that still matches it, and drops back to
    /// <see cref="BookingStatus.InProgress"/> the moment one of them doesn't.
    ///
    /// <para>
    /// That is deliberately per-booking rather than per-block: a group can read
    /// <see cref="BlockStatus.NeedsAmendment"/> because one booking changed while the others stay
    /// Confirmed — see <see cref="BlockSnapshot"/>. A group stored as
    /// <see cref="BlockStatus.InProgress"/> can read <see cref="BlockStatus.NeedsAmendment"/> too, but
    /// it never confirms a booking: it has recorded a request, not a supplier's agreement.
    /// </para>
    ///
    /// <para>
    /// <see cref="BookingStatus.Completed"/> and <see cref="BookingStatus.Cancelled"/> are operator-owned
    /// and are never written by that recalculation.
    /// </para>
    /// </summary>
    public interface IBlockStatusService
    {
        /// <summary>
        /// Every arrival group a booking belongs to, in itinerary order, each with its block status,
        /// documents, and whether that group's confirmation still covers this booking — what
        /// <c>Booking/Details</c> shows so an operator can see why a booking isn't confirmed yet.
        /// </summary>
        Task<IReadOnlyList<BookingBlockLine>> GetForBookingAsync(int bookingId);

        /// <summary>
        /// Upserts a group's status and email links, re-records the group as it now stands, then
        /// recalculates every booking in that group. Links are only ever written, never cleared — a
        /// blank field leaves the stored value alone.
        /// </summary>
        Task<OperationResult> SetStatusAsync(BlockStatusChange change);

        /// <summary>
        /// Re-derives <c>Booking.Status</c> from the confirmation records of the blocks each booking
        /// belongs to. Call after anything that changes a booking's block membership <i>or its rooms,
        /// PAX or nights</i> (create, edit, regenerate) as well as after a block status change. Does not
        /// call <c>CompleteAsync</c> — the caller saves, so this can join an existing unit of work.
        /// </summary>
        Task RecalculateAsync(IEnumerable<int> bookingIds);
    }
}
