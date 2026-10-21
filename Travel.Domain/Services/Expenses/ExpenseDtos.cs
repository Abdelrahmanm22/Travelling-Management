using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.Expenses
{
    /// <summary>An arrival group's identity — the same triple Block Services keys everything on.</summary>
    public sealed record ExpenseGroupKey(
        int DestinationId, DateOnly CheckInDate, PackageCategory Category, int? PrivateBookingId = null)
    {
        /// <summary>
        /// How the key travels through a checkbox value and back: "12|2026-04-12|5|" for a shared
        /// block, "12|2026-04-12|5|37" for booking 37's private file.
        /// </summary>
        public string Token => $"{DestinationId}|{CheckInDate:yyyy-MM-dd}|{(int)Category}|{PrivateBookingId}";

        public BlockKey Block => new(DestinationId, CheckInDate, Category, PrivateBookingId);

        public static ExpenseGroupKey From(BlockKey key) =>
            new(key.DestinationId, key.CheckInDate, key.Category, key.PrivateBookingId);

        public static ExpenseGroupKey? Parse(string? token)
        {
            var parts = token?.Split('|');
            if (parts is not { Length: 4 }) return null;

            int? privateBookingId = null;
            if (parts[3].Length > 0)
            {
                if (!int.TryParse(parts[3], out var id)) return null;
                privateBookingId = id;
            }

            return int.TryParse(parts[0], out var destinationId)
                && DateOnly.TryParse(parts[1], out var checkIn)
                && int.TryParse(parts[2], out var category)
                && Enum.IsDefined((PackageCategory)category)
                    ? new ExpenseGroupKey(destinationId, checkIn, (PackageCategory)category, privateBookingId)
                    : null;
        }
    }

    /// <param name="Rates">Per-room only. A <c>null</c> value means "not entered" — 0 is a legal, free rate.</param>
    /// <param name="LumpSumTotal">Per-pax only: one total for the WHOLE expense, split across every selected group (or booking).</param>
    /// <param name="Target">Which of <paramref name="Groups"/> / <paramref name="Bookings"/> is read; the other is ignored.</param>
    /// <param name="Bookings">Booking ids, for a <see cref="ExpenseTarget.Bookings"/> expense.</param>
    public sealed record ExpenseCalculationRequest(
        ExpenseBasis Basis,
        bool IncludeChildren,
        bool IncludeInfants,
        IReadOnlyList<ExpenseGroupKey> Groups,
        IReadOnlyDictionary<RoomType, decimal?> Rates,
        decimal? LumpSumTotal,
        ExpenseTarget Target = ExpenseTarget.ArrivalGroups,
        IReadOnlyList<int>? Bookings = null);

    /// <param name="Key">
    /// A ModelState key relative to the expense form — "Rates[1].Rate", "SelectedGroupKeys",
    /// "SelectedBookingIds", "LumpSumTotal", or "" for a model-level error. Handed to <c>ModelState.AddModelError</c>
    /// verbatim, which is why every one of them needs a matching validation span in the markup.
    /// </param>
    public sealed record ExpenseValidationError(string Key, string Message);

    public sealed record ExpenseBookingAllocation(
        int BookingId,
        string Code,
        string LeadGuestName,
        int Adults,
        int Children,
        int Infants,
        int EligiblePax,
        int Nights,
        IReadOnlyList<RoomTally> Rooms,
        string RoomsFingerprint,
        decimal Amount,
        string AppliedFingerprint = "");

    public sealed record ExpenseGroupAllocation(
        int DestinationId,
        string DestinationName,
        DateOnly CheckInDate,
        PackageCategory Category,
        int? PrivateBookingId,
        int BookingCount,
        int Adults,
        int Children,
        int Infants,
        int EligiblePax,
        IReadOnlyList<RoomTally> Rooms,
        string RoomsFingerprint,
        string AppliedFingerprint,
        decimal Amount,
        IReadOnlyList<ExpenseBookingAllocation> Bookings);

    /// <param name="DirectBookings">
    /// The allocation of a booking-target expense, one line per booking (each carrying its own
    /// <c>AppliedFingerprint</c>); empty for an arrival-group expense, whose lines sit under
    /// <paramref name="Groups"/>.
    /// </param>
    public sealed record ExpenseCalculation(
        decimal Total,
        IReadOnlyList<ExpenseGroupAllocation> Groups,
        IReadOnlyList<ExpenseBookingAllocation> DirectBookings);

    /// <summary>
    /// Not <c>OperationResult&lt;T&gt;</c>: that carries flat strings with no key, and every guard here
    /// has to land on a specific input — several of them collection-indexed — or the rule is invisible,
    /// which is this codebase's single most-repeated bug.
    /// </summary>
    public sealed record ExpenseCalculationResult(
        bool Succeeded,
        ExpenseCalculation? Calculation,
        IReadOnlyList<ExpenseValidationError> Errors)
    {
        public static ExpenseCalculationResult Ok(ExpenseCalculation calculation) =>
            new(true, calculation, Array.Empty<ExpenseValidationError>());

        public static ExpenseCalculationResult Fail(IReadOnlyList<ExpenseValidationError> errors) =>
            new(false, null, errors);

        public static ExpenseCalculationResult Fail(string key, string message) =>
            Fail(new[] { new ExpenseValidationError(key, message) });
    }
}
