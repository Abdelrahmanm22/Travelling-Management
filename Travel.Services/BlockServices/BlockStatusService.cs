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

            var keys = stays.Select(s => KeyOf(booking, s)).ToList();
            var rows = await _groups.LoadStatusesAsync(keys);

            // A block's displayed status depends on the WHOLE group, not just this booking, so the
            // other bookings sharing each of these arrival dates have to be loaded too.
            var groups = await _groups.LoadGroupStaysAsync(keys);

            return stays
                .Select(s =>
                {
                    var key = KeyOf(booking, s);
                    var row = rows.GetValueOrDefault(key);
                    var resolution = BlockSnapshot.Resolve(
                        row?.Status ?? BlockStatus.NotBooked,
                        groups.GetValueOrDefault(key) ?? new List<BookingStay>(),
                        row?.RecordedBookings.ToList() ?? new List<BlockRecordedBooking>());

                    return new BookingBlockLine(
                        DestinationId: s.DestinationId,
                        DestinationName: s.Destination?.Name?.Trim() is { Length: > 0 } name ? name : "—",
                        CheckInDate: s.CheckInDate,
                        Category: booking.Category,
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

            // Derived, never stored: a block reads "needs amendment" because what the supplier was told
            // stopped matching, and the only way out is to re-save the status it already has, which
            // re-records the group. Offering it as a choice would let an operator park a block in a
            // state nothing can move it out of.
            if (change.Status == BlockStatus.NeedsAmendment)
                return OperationResult.Fail("Needs amendment is derived from what the supplier was told — it can't be set by hand.");

            var requestLink = Normalize(change.RequestEmailLink);
            var confirmationLink = Normalize(change.ConfirmationEmailLink);

            if (!IsAcceptableLink(requestLink))
                return OperationResult.Fail("The booking request email link must be a valid http or https URL.");

            if (!IsAcceptableLink(confirmationLink))
                return OperationResult.Fail("The booking confirmation email link must be a valid http or https URL.");

            var repository = _unitOfWork.Repository<BlockServiceStatus>();

            // Writes the status row, its record of what the supplier was told and every affected
            // booking — more than one CompleteAsync, so it needs to be atomic.
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var row = await repository.GetByIdAsync(
                    new BlockStatusSpec(change.DestinationId, change.CheckInDate, change.Category));

                if (row is null)
                {
                    // Created lazily: a group only gets a row once somebody moves it off "Not booked"
                    // or uploads a document to it.
                    row = new BlockServiceStatus
                    {
                        DestinationId = change.DestinationId,
                        CheckInDate = change.CheckInDate,
                        Category = change.Category,
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
                    .GetAllAsync(new BlockStaysSpec(change.DestinationId, change.CheckInDate, change.Category))).ToList();

                await WriteRecordAsync(row, stays);
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
                recalculable.SelectMany(b => b.Stays.Select(s => KeyOf(b, s))));

            foreach (var booking in recalculable)
            {
                // One stay per arrival group — a booking visiting the same destination twice on the
                // same check-in has a single snapshot row, matching how the block rolls it up.
                var groups = booking.Stays
                    .GroupBy(s => KeyOf(booking, s))
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
        /// the record rather than the block's resolved status: the block may read "needs amendment"
        /// because a <i>different</i> booking changed, which must not un-confirm this one.
        ///
        /// <para>
        /// The stored status must be Confirmed. An In-progress block also keeps a record, but it is a
        /// record of what we <i>asked</i> for — no supplier has agreed to it, so it must never confirm
        /// a booking.
        /// </para>
        /// </summary>
        private static bool IsCovered(
            Dictionary<BlockKey, BlockServiceStatus> rows, Booking booking, BookingStay stay)
        {
            if (!rows.TryGetValue(KeyOf(booking, stay), out var row))
                return false;

            if (row.Status != BlockStatus.Confirmed)
                return false;

            var snapshot = row.RecordedBookings.FirstOrDefault(c => c.BookingId == booking.Id);
            return snapshot is not null && BlockSnapshot.Matches(snapshot, booking, stay.Nights);
        }

        /// <summary>
        /// Records what the supplier was told — the group exactly as it stands now — or clears it.
        /// Rows exist while the block is In progress (what the request described) or Confirmed (what
        /// the supplier confirmed); moving to Not booked means nothing has been told to anybody, so
        /// there is nothing to compare against. Re-saving the same status deliberately overwrites the
        /// old record, which is the "I re-sent the request" / "I re-confirmed" action.
        /// </summary>
        private async Task WriteRecordAsync(BlockServiceStatus row, IReadOnlyList<BookingStay> stays)
        {
            var repository = _unitOfWork.Repository<BlockRecordedBooking>();

            foreach (var existing in row.RecordedBookings.ToList())
            {
                repository.Delete(existing);
                row.RecordedBookings.Remove(existing);
            }

            if (row.Status is not (BlockStatus.InProgress or BlockStatus.Confirmed)) return;

            // One row per booking, not per stay — the same guard DistinctByBooking applies on the read
            // side, for a booking that visits this destination twice on the same check-in.
            foreach (var group in stays.GroupBy(s => s.BookingId))
            {
                var stay = group.First();
                var snapshot = BlockSnapshot.Take(stay.Booking, stay.Nights);
                snapshot.BlockServiceStatusId = row.Id;
                await repository.AddAsync(snapshot);
            }
        }

        /// <summary>
        /// The group a stay belongs to. The category is read off the booking explicitly rather than
        /// through <c>stay.Booking</c>, so it can't depend on whether that back-navigation was fixed up.
        /// </summary>
        private static BlockKey KeyOf(Booking booking, BookingStay stay) =>
            new(stay.DestinationId, stay.CheckInDate, booking.Category);

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
