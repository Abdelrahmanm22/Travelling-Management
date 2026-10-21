using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.Expenses;
using Travel.Domain.Specifications.Expenses;

namespace Travel.Services.Expenses
{
    /// <inheritdoc cref="IExpenseCalculator" />
    public class ExpenseCalculator : IExpenseCalculator
    {
        private readonly IBlockServiceQuery _blocks;
        private readonly IUnitOfWork _unitOfWork;

        public ExpenseCalculator(IBlockServiceQuery blocks, IUnitOfWork unitOfWork)
        {
            _blocks = blocks;
            _unitOfWork = unitOfWork;
        }

        /// <inheritdoc />
        public Task<ExpenseCalculationResult> CalculateAsync(ExpenseCalculationRequest request) =>
            request.Target == ExpenseTarget.Bookings
                ? CalculateForBookingsAsync(request)
                : CalculateForGroupsAsync(request);

        // ─── Arrival groups ─────────────────────────────────────────────────

        private static readonly Subject GroupsSubject =
            new(nameof(ExpenseCalculationRequest.Groups), "arrival groups");

        private async Task<ExpenseCalculationResult> CalculateForGroupsAsync(ExpenseCalculationRequest request)
        {
            var keys = request.Groups
                .DistinctBy(g => g.Block)
                .ToList();

            if (keys.Count == 0)
                return ExpenseCalculationResult.Fail(GroupsSubject.Key, "Select at least one arrival group.");

            var errors = new List<ExpenseValidationError>();
            var files = new List<BlockFile>();

            foreach (var key in keys)
            {
                var file = await _blocks.GetBlockAsync(key.Block);

                // The group emptied between the picker rendering and the post — a real possibility,
                // since arrival groups are derived from live booking data on every request.
                if (file is null)
                    errors.Add(new ExpenseValidationError(
                        GroupsSubject.Key,
                        $"The {(key.Category == PackageCategory.FiveStars ? "5★" : "4★")}{(key.PrivateBookingId.HasValue ? " private" : "")} arrival group for {key.CheckInDate:dd MMM yyyy} no longer has any booking."));
                else
                    files.Add(file);
            }

            if (errors.Count > 0)
                return ExpenseCalculationResult.Fail(errors);

            // 🔴 Indexed by POSITION, not by booking id. The same booking legitimately appears in
            // several selected groups — a Cairo + Cruise booking is in both — and each of those is its
            // own allocation line. Keying on the booking id alone silently collapses them (and threw
            // outright on the duplicate key, which is how this was caught).
            var lines = files
                .SelectMany((file, fileIndex) => file.Bookings.Select(line => (Slot: fileIndex, Line: line)))
                .Select((x, index) => new ChargeLine(
                    Index: index,
                    Slot: x.Slot,
                    BookingId: x.Line.BookingId,
                    Code: x.Line.Code,
                    LeadGuestName: x.Line.LeadGuestName,
                    Adults: x.Line.Adults,
                    Children: x.Line.Children,
                    Infants: x.Line.Infants,
                    // That booking's nights IN THIS GROUP — bookings in one group routinely stay
                    // different lengths.
                    Nights: x.Line.Nights,
                    Rooms: x.Line.Rooms,
                    AppliedFingerprint: string.Empty))
                .ToList();

            var allocated = Allocate(request, lines, GroupsSubject);
            if (!allocated.Succeeded)
                return ExpenseCalculationResult.Fail(allocated.Errors);

            var groups = allocated.Lines
                .GroupBy(a => a.Line.Slot)
                .OrderBy(g => g.Key)
                .Select(g => GroupAllocation(files[g.Key], g.Select(a => a.Allocation).ToList()))
                .ToList();

            return ExpenseCalculationResult.Ok(new ExpenseCalculation(
                groups.Sum(g => g.Amount), groups, Array.Empty<ExpenseBookingAllocation>()));
        }

        // ─── Bookings ───────────────────────────────────────────────────────
        //
        // The same two formulas over the selected bookings themselves. Per room multiplies by the
        // booking's WHOLE-TRIP nights (Σ stays); per pax pools every selected booking's eligible pax.

        private static readonly Subject BookingsSubject =
            new(nameof(ExpenseCalculationRequest.Bookings), "bookings");

        private async Task<ExpenseCalculationResult> CalculateForBookingsAsync(ExpenseCalculationRequest request)
        {
            var ids = (request.Bookings ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return ExpenseCalculationResult.Fail(BookingsSubject.Key, "Select at least one booking.");

            var bookings = (await _unitOfWork.Repository<Booking>()
                    .GetAllAsync(new BookingsForExpenseSpec(ids)))
                .ToDictionary(b => b.Id);

            if (ids.Any(id => !bookings.ContainsKey(id)))
                return ExpenseCalculationResult.Fail(
                    BookingsSubject.Key, "One of the selected bookings no longer exists — reload the list and try again.");

            // In the order the bookings arrive, so the preview and the stored lines list them the way
            // the picker did. Index is position — it terminates the per-pax tie-break.
            var lines = ids
                .Select(id => bookings[id])
                .OrderBy(b => b.ArrivalDate)
                .ThenBy(b => b.Code, StringComparer.Ordinal)
                .Select((b, index) => new ChargeLine(
                    Index: index,
                    Slot: 0,
                    BookingId: b.Id,
                    Code: b.Code,
                    LeadGuestName: b.LeadGuestName,
                    Adults: b.Adults,
                    Children: b.Children,
                    Infants: b.Infants,
                    Nights: b.TripNights,
                    Rooms: TallyRooms(b.Rooms),
                    AppliedFingerprint: ExpenseFingerprint.ForBooking(b)))
                .ToList();

            var allocated = Allocate(request, lines, BookingsSubject);
            if (!allocated.Succeeded)
                return ExpenseCalculationResult.Fail(allocated.Errors);

            var direct = allocated.Lines.Select(a => a.Allocation).ToList();

            return ExpenseCalculationResult.Ok(new ExpenseCalculation(
                direct.Sum(d => d.Amount), Array.Empty<ExpenseGroupAllocation>(), direct));
        }

        // ─── The two formulas ───────────────────────────────────────────────

        private static Allocated Allocate(
            ExpenseCalculationRequest request, IReadOnlyList<ChargeLine> lines, Subject subject) =>
            request.Basis == ExpenseBasis.PerRoom
                ? PerRoom(request, lines, subject)
                : PerPax(request, lines, subject);

        // Per room — rate is per room PER NIGHT, and each line multiplies by ITS OWN nights. Derived
        // bottom-up, so the group and grand totals are exact by construction and need no residual pass.

        private static Allocated PerRoom(
            ExpenseCalculationRequest request, IReadOnlyList<ChargeLine> lines, Subject subject)
        {
            var errors = new List<ExpenseValidationError>();

            // Every room type actually present across the selection needs a rate. A rate of 0 is legal
            // (deliberately free); only an ABSENT one is an error, which is why Rates holds decimal?.
            var usedTypes = lines
                .SelectMany(l => l.Rooms)
                .Where(t => t.Quantity > 0)
                .Select(t => t.Type)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (usedTypes.Count == 0)
                errors.Add(new ExpenseValidationError(
                    subject.Key,
                    $"The selected {subject.Noun} have no rooms recorded, so a per-room expense can't be applied to them."));

            foreach (var type in usedTypes)
            {
                if (request.Rates.TryGetValue(type, out var rate) && rate.HasValue)
                    continue;

                // Keyed to the room type — the controller maps it onto Rates[i].Rate so the message
                // lands under the input it is about.
                errors.Add(new ExpenseValidationError(
                    RateKey(type),
                    $"Enter a rate for {RoomAbbreviation(type)} — the selected {subject.Noun} include rooms of this type."));
            }

            if (errors.Count > 0)
                return Allocated.Fail(errors);

            var allocations = lines
                .Select(line =>
                {
                    var perNight = line.Rooms.Sum(t => Rate(request.Rates, t.Type) * t.Quantity);
                    var amount = Round(perNight * line.Nights);
                    return (line, Allocation(line, EligiblePax(line, request), amount));
                })
                .ToList();

            if (allocations.Sum(a => a.Item2.Amount) <= 0m)
                return Allocated.Fail(new[]
                {
                    new ExpenseValidationError(string.Empty, "This expense works out to 0.00 — check the rates you entered."),
                });

            return Allocated.Ok(allocations);
        }

        // Per pax — one lump sum for the WHOLE expense, divided across the combined eligible pax of
        // every selected line — one pool, not one division per group. Allocating at the line level and
        // rolling up is what makes the group totals AND the grand total reconcile to the entered
        // figure exactly, in a single rounding pass.

        private static Allocated PerPax(
            ExpenseCalculationRequest request, IReadOnlyList<ChargeLine> lines, Subject subject)
        {
            if (request.LumpSumTotal is not { } total || total <= 0m)
                return Allocated.Fail(new[]
                {
                    new ExpenseValidationError(
                        nameof(ExpenseCalculationRequest.LumpSumTotal), "Enter the total amount for this expense."),
                });

            var eligible = lines.ToDictionary(l => l.Index, l => EligiblePax(l, request));
            var pool = eligible.Values.Sum();

            // Guarded BEFORE the division is reached — never a caught DivideByZeroException.
            if (pool == 0)
                return Allocated.Fail(new[]
                {
                    new ExpenseValidationError(
                        subject.Key,
                        $"The selected {subject.Noun} have no passengers this expense type applies to. "
                        + "Check whether children and infants should be included on the expense type."),
                });

            // Largest remainder at 2 dp: floor everything to whole cents, then hand the leftover cents
            // out one at a time by descending fractional part.
            var draft = lines
                .Select(line =>
                {
                    var raw = total * eligible[line.Index] / pool * 100m;
                    var cents = (long)Math.Floor(raw);
                    return new { Line = line, Eligible = eligible[line.Index], Cents = cents, Fraction = raw - cents };
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
                .ThenBy(d => d.Line.Index)
                .ToList();

            var extra = new HashSet<int>();
            for (var i = 0; i < residual && i < order.Count; i++)
                extra.Add(order[i].Line.Index);

            return Allocated.Ok(draft
                .Select(d => (d.Line, Allocation(
                    d.Line, d.Eligible, (d.Cents + (extra.Contains(d.Line.Index) ? 1 : 0)) / 100m)))
                .ToList());
        }

        // ─── Shared ─────────────────────────────────────────────────────────

        /// <summary>The key the per-room rate error lands on, resolved to a row index by the controller.</summary>
        public static string RateKey(RoomType type) => $"Rate.{(int)type}";

        /// <summary>
        /// Who this expense type charges for. Adults always count; the two flags come from the type
        /// (snapshotted onto the expense, so recalculating uses the rule that was actually applied).
        /// </summary>
        private static int EligiblePax(ChargeLine line, ExpenseCalculationRequest request) =>
            line.Adults
            + (request.IncludeChildren ? line.Children : 0)
            + (request.IncludeInfants ? line.Infants : 0);

        private static decimal Rate(IReadOnlyDictionary<RoomType, decimal?> rates, RoomType type) =>
            rates.TryGetValue(type, out var rate) ? rate ?? 0m : 0m;

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>Quantities per room type, SGL → DBL → TRP — the same tally Block Services builds.</summary>
        private static IReadOnlyList<RoomTally> TallyRooms(IEnumerable<BookingRoom> rooms) =>
            rooms.GroupBy(r => r.Type)
                 .Select(g => new RoomTally(g.Key, g.Sum(r => r.Quantity)))
                 .OrderBy(t => t.Type)
                 .ToList();

        private static ExpenseBookingAllocation Allocation(ChargeLine line, int eligible, decimal amount) =>
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
                Amount: amount,
                AppliedFingerprint: line.AppliedFingerprint);

        private static ExpenseGroupAllocation GroupAllocation(
            BlockFile file, IReadOnlyList<ExpenseBookingAllocation> bookings) =>
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

        /// <summary>
        /// One booking to charge. <c>Index</c> is its position in the whole selection (unique — the
        /// allocation key); <c>Slot</c> is the arrival group it belongs to, 0 for a booking expense.
        /// </summary>
        private sealed record ChargeLine(
            int Index,
            int Slot,
            int BookingId,
            string Code,
            string LeadGuestName,
            int Adults,
            int Children,
            int Infants,
            int Nights,
            IReadOnlyList<RoomTally> Rooms,
            string AppliedFingerprint);

        /// <summary>Which error key and wording a failure uses — groups and bookings word it differently.</summary>
        private sealed record Subject(string Key, string Noun);

        private sealed record Allocated(
            bool Succeeded,
            IReadOnlyList<(ChargeLine Line, ExpenseBookingAllocation Allocation)> Lines,
            IReadOnlyList<ExpenseValidationError> Errors)
        {
            public static Allocated Ok(IReadOnlyList<(ChargeLine, ExpenseBookingAllocation)> lines) =>
                new(true, lines, Array.Empty<ExpenseValidationError>());

            public static Allocated Fail(IReadOnlyList<ExpenseValidationError> errors) =>
                new(false, Array.Empty<(ChargeLine, ExpenseBookingAllocation)>(), errors);
        }
    }
}
