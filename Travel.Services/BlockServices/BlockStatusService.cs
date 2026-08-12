using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Specifications.BlockServices;
using Travel.Domain.Specifications.Bookings;

namespace Travel.Services.BlockServices
{
    /// <inheritdoc cref="IBlockStatusService" />
    public class BlockStatusService : IBlockStatusService
    {
        /// <summary>Matches the column length in BlockServiceStatusConfig.</summary>
        private const int MaxLinkLength = 1000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly BlockGroupLoader _groups;

        public BlockStatusService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _groups = new BlockGroupLoader(unitOfWork);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<BookingBlockLine>> GetForBookingAsync(int bookingId)
        {
            var booking = await _unitOfWork.Repository<Booking>()
                .GetByIdAsync(new BookingsForStatusSpec(bookingId));

            if (booking is null) return Array.Empty<BookingBlockLine>();

            var stays = booking.Stays.OrderBy(s => s.SortOrder).ToList();
            if (stays.Count == 0) return Array.Empty<BookingBlockLine>();

            var keys = stays.Select(s => (s.DestinationId, s.CheckInDate)).ToList();
            var rows = await _groups.LoadStatusesAsync(keys);

            // A block's displayed status depends on the WHOLE group, not just this booking, so the
            // other bookings sharing each of these arrival dates have to be loaded too.
            var groups = await _groups.LoadGroupStaysAsync(keys);

            return stays
                .Select(s =>
                {
                    var row = rows.GetValueOrDefault((s.DestinationId, s.CheckInDate));
                    var resolution = BlockConfirmation.Resolve(
                        row?.Status ?? BlockStatus.NotBooked,
                        groups.GetValueOrDefault((s.DestinationId, s.CheckInDate)) ?? new List<BookingStay>(),
                        row?.ConfirmedBookings.ToList() ?? new List<BlockConfirmedBooking>());

                    return new BookingBlockLine(
                        DestinationId: s.DestinationId,
                        DestinationName: s.Destination?.Name?.Trim() is { Length: > 0 } name ? name : "—",
                        CheckInDate: s.CheckInDate,
                        Nights: s.Nights,
                        Status: BlockStatusInfo.From(row).WithStatus(resolution.Status),
                        IsCovered: resolution.CoveredBookingIds.Contains(bookingId));
                })
                .ToList();
        }

        /// <inheritdoc />
        public async Task<OperationResult> SetStatusAsync(BlockStatusChange change)
        {
            if (!Enum.IsDefined(change.Status))
                return OperationResult.Fail("Unknown status.");

            // Derived, never stored: a block reads "needs amendment" because its confirmation stopped
            // matching, and the only way out is to confirm it again. Offering it as a choice would let
            // an operator park a block in a state nothing can move it out of.
            if (change.Status == BlockStatus.NeedsAmendment)
                return OperationResult.Fail("Needs amendment is derived from the block's confirmation — it can't be set by hand.");

            var requestLink = Normalize(change.RequestEmailLink);
            var confirmationLink = Normalize(change.ConfirmationEmailLink);

            if (!IsAcceptableLink(requestLink))
                return OperationResult.Fail("The booking request email link must be a valid http or https URL.");

            if (!IsAcceptableLink(confirmationLink))
                return OperationResult.Fail("The booking confirmation email link must be a valid http or https URL.");

            var repository = _unitOfWork.Repository<BlockServiceStatus>();

            // Writes the status row, its confirmation snapshot and every affected booking — more than
            // one CompleteAsync, so it needs to be atomic.
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var row = await repository.GetByIdAsync(
                    new BlockStatusSpec(change.DestinationId, change.CheckInDate));

                if (row is null)
                {
                    // Created lazily: a group only gets a row once somebody moves it off "Not booked"
                    // or uploads a document to it.
                    row = new BlockServiceStatus
                    {
                        DestinationId = change.DestinationId,
                        CheckInDate = change.CheckInDate,
                    };
                    await repository.AddAsync(row);
                }

                row.Status = change.Status;

                // Only ever written, never cleared. The two links are independent records of two
                // different emails: confirming a block must not lose the request thread, and moving
                // back to Not booked must not lose either.
                if (requestLink is not null) row.RequestEmailLink = requestLink;
                if (confirmationLink is not null) row.ConfirmationEmailLink = confirmationLink;

                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedByUserId = change.UserId;
                row.UpdatedByName = change.UserName;

                if (row.Id != 0) repository.Update(row);

                await _unitOfWork.CompleteAsync();

                var stays = (await _unitOfWork.Repository<BookingStay>()
                    .GetAllAsync(new BlockStaysSpec(change.DestinationId, change.CheckInDate))).ToList();

                await WriteSnapshotAsync(row, stays);
                await _unitOfWork.CompleteAsync();

                await RecalculateAsync(stays.Select(s => s.BookingId).Distinct().ToList());
                await _unitOfWork.CompleteAsync();

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult.Ok();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RecalculateAsync(IEnumerable<int> bookingIds)
        {
            var ids = bookingIds.Distinct().ToList();
            if (ids.Count == 0) return;

            var repository = _unitOfWork.Repository<Booking>();
            var bookings = await repository.GetAllAsync(new BookingsForStatusSpec(ids));

            // Completed and Cancelled are operator-owned: a block confirmation must never resurrect a
            // cancelled booking, and un-confirming a block must never reopen a completed one.
            var recalculable = bookings
                .Where(b => b.Status is BookingStatus.InProgress or BookingStatus.Confirmed)
                .ToList();

            if (recalculable.Count == 0) return;

            var rows = await _groups.LoadStatusesAsync(
                recalculable.SelectMany(b => b.Stays).Select(s => (s.DestinationId, s.CheckInDate)));

            foreach (var booking in recalculable)
            {
                // One stay per arrival group — a booking visiting the same destination twice on the
                // same check-in has a single snapshot row, matching how the block rolls it up.
                var groups = booking.Stays
                    .GroupBy(s => (s.DestinationId, s.CheckInDate))
                    .Select(g => g.First())
                    .ToList();

                // A booking with no stays has nothing to confirm against, so it stays In Progress.
                var allCovered = groups.Count > 0 && groups.All(stay => IsCovered(rows, booking, stay));

                var resolved = allCovered ? BookingStatus.Confirmed : BookingStatus.InProgress;
                if (booking.Status == resolved) continue;

                booking.Status = resolved;
                repository.Update(booking);
            }
        }

        /// <summary>
        /// Whether the supplier's confirmation for one arrival group still covers this booking. Reads
        /// the snapshot rather than the block's status: the block may read "needs amendment" because a
        /// <i>different</i> booking changed, which must not un-confirm this one.
        /// </summary>
        private static bool IsCovered(
            Dictionary<(int, DateOnly), BlockServiceStatus> rows, Booking booking, BookingStay stay)
        {
            if (!rows.TryGetValue((stay.DestinationId, stay.CheckInDate), out var row))
                return false;

            if (row.Status != BlockStatus.Confirmed)
                return false;

            var snapshot = row.ConfirmedBookings.FirstOrDefault(c => c.BookingId == booking.Id);
            return snapshot is not null && BlockConfirmation.Matches(snapshot, booking, stay.Nights);
        }

        /// <summary>
        /// Records what the supplier confirmed, or clears it. The snapshot only exists while the block
        /// is Confirmed — moving it anywhere else means there is nothing confirmed to compare against,
        /// and re-confirming deliberately overwrites the old snapshot with the group as it stands now.
        /// </summary>
        private async Task WriteSnapshotAsync(BlockServiceStatus row, IReadOnlyList<BookingStay> stays)
        {
            var repository = _unitOfWork.Repository<BlockConfirmedBooking>();

            foreach (var existing in row.ConfirmedBookings.ToList())
            {
                repository.Delete(existing);
                row.ConfirmedBookings.Remove(existing);
            }

            if (row.Status != BlockStatus.Confirmed) return;

            // One row per booking, not per stay — the same guard DistinctByBooking applies on the read
            // side, for a booking that visits this destination twice on the same check-in.
            foreach (var group in stays.GroupBy(s => s.BookingId))
            {
                var stay = group.First();
                var snapshot = BlockConfirmation.Snapshot(stay.Booking, stay.Nights);
                snapshot.BlockServiceStatusId = row.Id;
                await repository.AddAsync(snapshot);
            }
        }

        /// <summary>Blank input means "leave the stored link alone", so it collapses to null.</summary>
        private static string? Normalize(string? link) =>
            string.IsNullOrWhiteSpace(link) ? null : link.Trim();

        /// <summary>
        /// Links are rendered as anchors, so anything but absolute http/https is rejected — a
        /// "javascript:" value must never reach the page.
        /// </summary>
        private static bool IsAcceptableLink(string? link)
        {
            if (link is null) return true;
            if (link.Length > MaxLinkLength) return false;

            return Uri.TryCreate(link, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
