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

        public BlockStatusService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <inheritdoc />
        public async Task<BlockStatusInfo> GetAsync(int destinationId, DateOnly checkIn)
        {
            var row = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetByIdAsync(new BlockStatusSpec(destinationId, checkIn));

            return BlockStatusInfo.From(row);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<BookingBlockLine>> GetForBookingAsync(int bookingId)
        {
            var booking = await _unitOfWork.Repository<Booking>()
                .GetByIdAsync(new BookingsForStatusSpec(bookingId));

            if (booking is null) return Array.Empty<BookingBlockLine>();

            var stays = booking.Stays.OrderBy(s => s.SortOrder).ToList();
            if (stays.Count == 0) return Array.Empty<BookingBlockLine>();

            var statuses = await LoadStatusesAsync(stays.Select(s => (s.DestinationId, s.CheckInDate)));

            return stays
                .Select(s => new BookingBlockLine(
                    DestinationId: s.DestinationId,
                    DestinationName: s.Destination?.Name?.Trim() is { Length: > 0 } name ? name : "—",
                    CheckInDate: s.CheckInDate,
                    Nights: s.Nights,
                    Status: Lookup(statuses, s.DestinationId, s.CheckInDate)))
                .ToList();
        }

        /// <inheritdoc />
        public async Task<OperationResult> SetStatusAsync(BlockStatusChange change)
        {
            if (!Enum.IsDefined(change.Status))
                return OperationResult.Fail("Unknown status.");

            var requestLink = Normalize(change.RequestEmailLink);
            var confirmationLink = Normalize(change.ConfirmationEmailLink);

            if (!IsAcceptableLink(requestLink))
                return OperationResult.Fail("The booking request email link must be a valid http or https URL.");

            if (!IsAcceptableLink(confirmationLink))
                return OperationResult.Fail("The booking confirmation email link must be a valid http or https URL.");

            var repository = _unitOfWork.Repository<BlockServiceStatus>();

            // Writes the status row and every affected booking — more than one CompleteAsync, so it
            // needs to be atomic.
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var row = await repository.GetByIdAsync(
                    new BlockStatusSpec(change.DestinationId, change.CheckInDate));

                if (row is null)
                {
                    // Created lazily: a group only gets a row once somebody moves it off "Not booked".
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

                var stays = await _unitOfWork.Repository<BookingStay>()
                    .GetAllAsync(new BlockStaysSpec(change.DestinationId, change.CheckInDate));

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

            var statuses = await LoadStatusesAsync(
                recalculable.SelectMany(b => b.Stays).Select(s => (s.DestinationId, s.CheckInDate)));

            foreach (var booking in recalculable)
            {
                var keys = booking.Stays
                    .Select(s => (s.DestinationId, s.CheckInDate))
                    .Distinct()
                    .ToList();

                // A booking with no stays has nothing to confirm against, so it stays In Progress.
                var allConfirmed = keys.Count > 0
                    && keys.All(k => Lookup(statuses, k.DestinationId, k.CheckInDate).Status == BlockStatus.Confirmed);

                var resolved = allConfirmed ? BookingStatus.Confirmed : BookingStatus.InProgress;
                if (booking.Status == resolved) continue;

                booking.Status = resolved;
                repository.Update(booking);
            }
        }

        /// <summary>
        /// One query for an arbitrary set of (destination, check-in) keys: EF can't translate a
        /// composite-key Contains, so it narrows to the destinations and the date span they cover and
        /// the exact match happens here.
        /// </summary>
        private async Task<Dictionary<(int, DateOnly), BlockServiceStatus>> LoadStatusesAsync(
            IEnumerable<(int DestinationId, DateOnly CheckInDate)> keys)
        {
            var distinct = keys.Distinct().ToList();
            if (distinct.Count == 0) return new Dictionary<(int, DateOnly), BlockServiceStatus>();

            var destinationIds = distinct.Select(k => k.DestinationId).Distinct().ToList();
            var from = distinct.Min(k => k.CheckInDate);
            var to = distinct.Max(k => k.CheckInDate);

            var rows = await _unitOfWork.Repository<BlockServiceStatus>()
                .GetAllAsync(new BlockStatusSpec(destinationIds, from, to));

            return rows.ToDictionary(r => (r.DestinationId, r.CheckInDate));
        }

        private static BlockStatusInfo Lookup(
            Dictionary<(int, DateOnly), BlockServiceStatus> statuses, int destinationId, DateOnly checkIn) =>
            statuses.TryGetValue((destinationId, checkIn), out var row)
                ? BlockStatusInfo.From(row)
                : BlockStatusInfo.NotBooked;

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
