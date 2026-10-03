using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.Expenses;

namespace Travel.Services.Expenses
{
    /// <inheritdoc cref="IExpenseCalculator" />
    public class ExpenseCalculator : IExpenseCalculator
    {
        private readonly IBlockServiceQuery _blocks;

        public ExpenseCalculator(IBlockServiceQuery blocks) => _blocks = blocks;

        /// <inheritdoc />
        public async Task<ExpenseCalculationResult> CalculateAsync(ExpenseCalculationRequest request)
        {
            var keys = request.Groups
                .DistinctBy(g => g.Block)
                .ToList();

            if (keys.Count == 0)
                return ExpenseCalculationResult.Fail(
                    nameof(ExpenseCalculationRequest.Groups), "Select at least one arrival group.");

            var errors = new List<ExpenseValidationError>();
            var files = new List<BlockFile>();

            foreach (var key in keys)
            {
                var file = await _blocks.GetBlockAsync(key.Block);

                // The group emptied between the picker rendering and the post — a real possibility,
                // since arrival groups are derived from live booking data on every request.
                if (file is null)
                    errors.Add(new ExpenseValidationError(
                        nameof(ExpenseCalculationRequest.Groups),
                        $"The {(key.Category == PackageCategory.FiveStars ? "5★" : "4★")}{(key.PrivateBookingId.HasValue ? " private" : "")} arrival group for {key.CheckInDate:dd MMM yyyy} no longer has any booking."));
                else
                    files.Add(file);
            }

            if (errors.Count > 0)
                return ExpenseCalculationResult.Fail(errors);

            return request.Basis == ExpenseBasis.PerRoom
                ? PerRoom(request, files)
                : PerPax(request, files);
        }

        // ─── Per room ───────────────────────────────────────────────────────
        //
        // rate is per room PER NIGHT, and each booking multiplies by ITS OWN stay nights — bookings in
        // the same arrival group routinely stay different lengths. Derived bottom-up, so the group and
        // grand totals are exact by construction and need no residual pass.

        private static ExpenseCalculationResult PerRoom(
            ExpenseCalculationRequest request, IReadOnlyList<BlockFile> files)
        {
            var errors = new List<ExpenseValidationError>();

            // Every room type actually present across the selection needs a rate. A rate of 0 is legal
            // (deliberately free); only an ABSENT one is an error, which is why Rates holds decimal?.
            var usedTypes = files
                .SelectMany(f => f.Summary.Rooms)
                .Where(t => t.Quantity > 0)
                .Select(t => t.Type)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (usedTypes.Count == 0)
                errors.Add(new ExpenseValidationError(
                    nameof(ExpenseCalculationRequest.Groups),
                    "The selected arrival groups have no rooms recorded, so a per-room expense can't be applied to them."));

            foreach (var type in usedTypes)
            {
                if (request.Rates.TryGetValue(type, out var rate) && rate.HasValue)
                    continue;

                // Keyed to the room type — the controller maps it onto Rates[i].Rate so the message
                // lands under the input it is about.
                errors.Add(new ExpenseValidationError(
                    RateKey(type),
                    $"Enter a rate for {RoomAbbreviation(type)} — the selected groups include rooms of this type."));
            }

            if (errors.Count > 0)
                return ExpenseCalculationResult.Fail(errors);

            var groups = new List<ExpenseGroupAllocation>();

            foreach (var file in files)
            {
                var bookings = file.Bookings
                    .Select(line =>
                    {
                        var perNight = line.Rooms.Sum(t => Rate(request.Rates, t.Type) * t.Quantity);
                        var amount = Round(perNight * line.Nights);

                        return Allocation(line, EligiblePax(line, request), amount);
                    })
                    .ToList();

                groups.Add(GroupAllocation(file, request, bookings));
            }

            var total = groups.Sum(g => g.Amount);

            if (total <= 0m)
                return ExpenseCalculationResult.Fail(
                    string.Empty, "This expense works out to 0.00 — check the rates you entered.");

            return ExpenseCalculationResult.Ok(new ExpenseCalculation(total, groups));
        }

        // ─── Per pax ────────────────────────────────────────────────────────
        //
        // One lump sum for the WHOLE expense, divided across the combined eligible pax of every
        // selected group — one pool, not one division per group. Allocating at the booking level and
        // rolling up is what makes the group totals AND the grand total reconcile to the entered
        // figure exactly, in a single rounding pass.

        private static ExpenseCalculationResult PerPax(
            ExpenseCalculationRequest request, IReadOnlyList<BlockFile> files)
        {
            if (request.LumpSumTotal is not { } total || total <= 0m)
                return ExpenseCalculationResult.Fail(
                    nameof(ExpenseCalculationRequest.LumpSumTotal),
                    "Enter the total amount for this expense.");

            // 🔴 Indexed by POSITION, not by booking id. The same booking legitimately appears in
            // several selected groups — a Cairo + Cruise booking is in both — and each of those is its
            // own allocation line. Keying on the booking id alone silently collapses them (and threw
            // outright on the duplicate key, which is how this was caught).
            var lines = files
                .SelectMany((file, fileIndex) => file.Bookings.Select(line => new
                {
                    FileIndex = fileIndex,
                    Line = line,
                    Eligible = EligiblePax(line, request),
                }))
                .Select((x, index) => new { x.FileIndex, x.Line, x.Eligible, Index = index })
                .ToList();

            var pool = lines.Sum(x => x.Eligible);

            // Guarded BEFORE the division is reached — never a caught DivideByZeroException.
            if (pool == 0)
                return ExpenseCalculationResult.Fail(
                    nameof(ExpenseCalculationRequest.Groups),
                    "The selected arrival groups have no passengers this expense type applies to. "
                    + "Check whether children and infants should be included on the expense type.");

            // Largest remainder at 2 dp: floor everything to whole cents, then hand the leftover cents
            // out one at a time by descending fractional part. The tie-break must be TOTAL and
            // deterministic, or recalculating shuffles cents between bookings for no reason and the
            // reader sees changes that never happened.
            var draft = lines
                .Select(x =>
                {
                    var raw = total * x.Eligible / pool * 100m;
                    var cents = (long)Math.Floor(raw);
                    return new { x.FileIndex, x.Line, x.Eligible, x.Index, Cents = cents, Fraction = raw - cents };
                })
                .ToList();

            var residual = (long)Math.Round(total * 100m, MidpointRounding.AwayFromZero)
                         - draft.Sum(d => d.Cents);

            // The tie-break must be TOTAL and deterministic, or recalculating shuffles cents between
            // bookings for no reason and the reader sees changes that never happened. Index is unique,
            // so it terminates the ordering.
            var order = draft
                .OrderByDescending(d => d.Fraction)
                .ThenByDescending(d => d.Eligible)
                .ThenBy(d => d.Line.BookingId)
                .ThenBy(d => d.Index)
                .ToList();

            var extra = new HashSet<int>();
            for (var i = 0; i < residual && i < order.Count; i++)
                extra.Add(order[i].Index);

            var amounts = draft.ToDictionary(
                d => d.Index,
                d => (d.Cents + (extra.Contains(d.Index) ? 1 : 0)) / 100m);

            var groups = draft
                .GroupBy(d => d.FileIndex)
                .OrderBy(g => g.Key)
                .Select(g => GroupAllocation(
                    files[g.Key],
                    request,
                    g.Select(d => Allocation(d.Line, d.Eligible, amounts[d.Index])).ToList()))
                .ToList();

            return ExpenseCalculationResult.Ok(new ExpenseCalculation(groups.Sum(g => g.Amount), groups));
        }

        // ─── Shared ─────────────────────────────────────────────────────────

        /// <summary>The key the per-room rate error lands on, resolved to a row index by the controller.</summary>
        public static string RateKey(RoomType type) => $"Rate.{(int)type}";

        /// <summary>
        /// Who this expense type charges for. Adults always count; the two flags come from the type
        /// (snapshotted onto the expense, so recalculating uses the rule that was actually applied).
        /// </summary>
        private static int EligiblePax(BlockBookingLine line, ExpenseCalculationRequest request) =>
            line.Adults
            + (request.IncludeChildren ? line.Children : 0)
            + (request.IncludeInfants ? line.Infants : 0);

        private static decimal Rate(IReadOnlyDictionary<RoomType, decimal?> rates, RoomType type) =>
            rates.TryGetValue(type, out var rate) ? rate ?? 0m : 0m;

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static ExpenseBookingAllocation Allocation(
            BlockBookingLine line, int eligible, decimal amount) =>
            new(
                BookingId: line.BookingId,
                Code: line.Code,
                LeadGuestName: line.LeadGuestName,
                Adults: line.Adults,
                Children: line.Children,
                Infants: line.Infants,
                EligiblePax: eligible,
                Nights: line.Nights,
                Rooms: line.Rooms,
                RoomsFingerprint: BlockSnapshot.RoomsFingerprint(line.Rooms),
                Amount: amount);

        private static ExpenseGroupAllocation GroupAllocation(
            BlockFile file,
            ExpenseCalculationRequest request,
            IReadOnlyList<ExpenseBookingAllocation> bookings) =>
            new(
                DestinationId: file.Summary.DestinationId,
                DestinationName: file.Summary.DestinationName,
                CheckInDate: file.Summary.CheckInDate,
                Category: file.Summary.Category,
                PrivateBookingId: file.Summary.PrivateBookingId,
                BookingCount: file.Summary.BookingCount,
                Adults: file.Summary.Adults,
                Children: file.Summary.Children,
                Infants: file.Summary.Infants,
                EligiblePax: bookings.Sum(b => b.EligiblePax),
                Rooms: file.Summary.Rooms,
                RoomsFingerprint: BlockSnapshot.RoomsFingerprint(file.Summary.Rooms),
                AppliedFingerprint: ExpenseFingerprint.FromBookingLines(file.Bookings),
                // Never an independent rounding — the group is exactly what its bookings came to.
                Amount: bookings.Sum(b => b.Amount),
                Bookings: bookings);

        private static string RoomAbbreviation(RoomType type) => type switch
        {
            RoomType.Single => "SGL",
            RoomType.Double => "DBL",
            RoomType.Triple => "TRP",
            _ => type.ToString(),
        };
    }
}
