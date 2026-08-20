using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.Expenses
{
    /// <summary>An arrival group's identity — the same pair Block Services keys everything on.</summary>
    public sealed record ExpenseGroupKey(int DestinationId, DateOnly CheckInDate)
    {
        /// <summary>How the key travels through a checkbox value and back: "12|2026-04-12".</summary>
        public string Token => $"{DestinationId}|{CheckInDate:yyyy-MM-dd}";

        public static ExpenseGroupKey? Parse(string? token)
        {
            var parts = token?.Split('|');
            if (parts is not { Length: 2 }) return null;

            return int.TryParse(parts[0], out var destinationId)
                && DateOnly.TryParse(parts[1], out var checkIn)
                    ? new ExpenseGroupKey(destinationId, checkIn)
                    : null;
        }
    }

    /// <param name="Rates">Per-room only. A <c>null</c> value means "not entered" — 0 is a legal, free rate.</param>
    /// <param name="LumpSumTotal">Per-pax only: one total for the WHOLE expense, split across every selected group.</param>
    public sealed record ExpenseCalculationRequest(
        ExpenseBasis Basis,
        bool IncludeChildren,
        bool IncludeInfants,
        IReadOnlyList<ExpenseGroupKey> Groups,
        IReadOnlyDictionary<RoomType, decimal?> Rates,
        decimal? LumpSumTotal);

    /// <param name="Key">
    /// A ModelState key relative to the expense form — "Rates[1].Rate", "SelectedGroupKeys",
    /// "LumpSumTotal", or "" for a model-level error. Handed to <c>ModelState.AddModelError</c>
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
        decimal Amount);

    public sealed record ExpenseGroupAllocation(
        int DestinationId,
        string DestinationName,
        DateOnly CheckInDate,
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

    public sealed record ExpenseCalculation(
        decimal Total,
        IReadOnlyList<ExpenseGroupAllocation> Groups);

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
