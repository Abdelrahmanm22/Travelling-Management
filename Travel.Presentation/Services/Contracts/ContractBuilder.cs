using System.Globalization;
using Travel.Domain;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Specifications.Bookings;
using Travel.Domain.Specifications.Contracts;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>A fresh contract built from a booking, plus what the page needs to say about it.</summary>
    public sealed class ContractBuild
    {
        public required Booking Booking { get; init; }
        public required ContractDocument Document { get; init; }
        public required ContractBasis Basis { get; init; }
        public required List<ContractWarning> Warnings { get; init; }
        public bool HasProgram { get; init; }
    }

    /// <summary>
    /// Builds a fresh <see cref="ContractDocument"/> from a booking and its package's contract program —
    /// the one owner of the rules in spec §5 (legs, free days, payment, lists, accommodation), so the
    /// page, the preview and Rebuild can't disagree.
    /// </summary>
    public sealed class ContractBuilder
    {
        private readonly IUnitOfWork _unitOfWork;

        public ContractBuilder(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        /// <summary>
        /// Loads the booking through <see cref="BookingWithDetailsSpec"/> — it reaches Stays.Rates, Tours
        /// and Transactions, without which <c>GrandTotal</c> / <c>PaidTotal</c> read silently too low.
        /// Null when the booking doesn't exist.
        /// </summary>
        public async Task<ContractBuild?> BuildAsync(int bookingId)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(bookingId));
            if (booking is null) return null;

            var package = await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageContractProgramSpec(booking.PackageId));
            return Build(booking, package!);
        }

        public static ContractBuild Build(Booking booking, Package package)
        {
            var warnings = new List<ContractWarning>();
            var basis = new ContractBasis();
            var tripTitle = string.IsNullOrWhiteSpace(package.ContractTitle) ? package.Name.Trim() : package.ContractTitle.Trim();
            var hasProgram = ContractProgramLegs.HasProgram(package);

            if (booking.Status == BookingStatus.Cancelled)
                warnings.Add(new(ContractWarning.Danger, "This booking is cancelled. A cancelled booking has no contract."));

            var doc = new ContractDocument
            {
                PartyName = booking.Guests.Count == 0 ? string.Empty : booking.LeadGuestName,
                GroupLabel = ContractText.GroupLabel(booking.ArrivalDate),
                TripTitle = tripTitle,
                Important = package.ContractImportantText?.Trim() ?? string.Empty,
                Documents = package.ContractDocumentsText?.Trim() ?? string.Empty,
                Date = ContractText.Today(),
            };

            BuildPassengers(booking, doc, basis);
            BuildPayment(booking, tripTitle, doc, basis, warnings);
            BuildSchedule(booking, package, hasProgram, doc, basis, warnings);
            BuildLists(booking, package, doc, basis, warnings);
            BuildAccommodation(booking, doc, basis, warnings);

            return new ContractBuild { Booking = booking, Document = doc, Basis = basis, Warnings = warnings, HasProgram = hasProgram };
        }

        // ── Passengers (decision 13) ────────────────────────────────────────────────────────────────

        private static void BuildPassengers(Booking booking, ContractDocument doc, ContractBasis basis)
        {
            foreach (var guest in booking.Guests.OrderBy(g => g.SortOrder))
            {
                var passenger = new ContractPassenger { Name = guest.FullName.Trim(), TypeLabel = ContractText.GuestType(guest.Type) };
                doc.Passengers.Add(passenger);
                basis.Passengers.Add($"{passenger.Name}|{passenger.TypeLabel}");
            }
        }

        // ── Payment (decision 7) ────────────────────────────────────────────────────────────────────

        private static void BuildPayment(Booking booking, string tripTitle, ContractDocument doc, ContractBasis basis, List<ContractWarning> warnings)
        {
            var payment = doc.Payment;
            payment.Title = ContractText.PaymentTitle(tripTitle);

            void Pax(int count, GuestType type, decimal? price)
            {
                if (count <= 0) return;
                payment.Lines.Add(new ContractPaymentLine
                {
                    Label = ContractText.PaxLine(count, type, price),
                    Amount = price is { } p ? count * p : null,
                });
            }

            Pax(booking.Adults, GuestType.Adult, booking.AdultPrice);
            Pax(booking.Children, GuestType.Child, booking.ChildPrice);
            Pax(booking.Infants, GuestType.Infant, booking.InfantPrice);

            if (booking.ExtraNightsTotal > 0)
                payment.Lines.Add(new ContractPaymentLine { Label = ContractText.ExtraNightsLabel, Amount = booking.ExtraNightsTotal });
            if (booking.ToursTotal > 0)
                payment.Lines.Add(new ContractPaymentLine { Label = ContractText.ToursLabel, Amount = booking.ToursTotal });

            // IsPriced also covers chargeable extra nights with no room rates.
            payment.Total = booking.IsPriced ? booking.GrandTotal : null;
            payment.Paid = booking.PaidTotal;
            payment.Balance = booking.IsPriced ? booking.Balance : null;

            if (!booking.IsPriced)
                warnings.Add(new(ContractWarning.Danger,
                    "This booking isn't fully priced. Price it on the booking before generating the contract.",
                    ContractSections.Payment));

            basis.Payment.AddRange(payment.Lines.Select(l => $"{l.Label}={Fact(l.Amount)}"));
            basis.Payment.Add($"total={Fact(payment.Total)}");
            basis.Payment.Add($"paid={Fact(payment.Paid)}");
            basis.Payment.Add($"balance={Fact(payment.Balance)}");
        }

        private static string Fact(decimal? amount) =>
            amount is { } a ? a.ToString("0.00", CultureInfo.InvariantCulture) : "-";

        // ── Schedule (decisions 1 + 2, spec §5.1–5.2) ───────────────────────────────────────────────

        private static void BuildSchedule(Booking booking, Package package, bool hasProgram, ContractDocument doc,
            ContractBasis basis, List<ContractWarning> warnings)
        {
            var legs = ContractProgramLegs.For(package);
            var legRows = package.ContractLegs.ToDictionary(l => l.Leg);
            var stays = booking.Stays.OrderBy(s => s.SortOrder).ToList();

            if (!hasProgram)
                warnings.Add(new(ContractWarning.Warning,
                    "This package has no contract program yet — fill it in Setup ▸ Packages. You can still type the days here.",
                    ContractSections.Schedule));
            else if (ContractProgramLegs.IsOutOfDate(package, legs))
                warnings.Add(new(ContractWarning.Warning,
                    "The package's itinerary changed since its contract program was written. Check the program in Setup ▸ Packages.",
                    ContractSections.Schedule));

            var changedDayCount = false;

            foreach (var step in Align(stays, legs))
            {
                if (step.Stay is null)
                {
                    // A leg the booking doesn't have. Silent when nothing was written for it — a 0-night
                    // lead the generator skipped is routine.
                    var unused = package.ContractDays.Count(d => d.Leg == step.Leg!.Leg);
                    if (unused > 0)
                        warnings.Add(new(ContractWarning.Warning,
                            $"{step.Leg!.Label}: the booking has no stay for this leg, so its {unused} program day(s) were left out.",
                            ContractSections.Schedule));
                    continue;
                }

                var stay = step.Stay;
                var destination = stay.Destination?.Name.Trim() ?? "Destino";
                var section = new ContractSection { Nights = stay.Nights };
                int inserted = 0, removed = 0;

                if (step.Leg is null)
                {
                    // A stay the operator added by hand: every night is a free day of its destination.
                    section.Heading = ContractText.SectionHeading(destination);
                    if (hasProgram)
                    {
                        for (var i = 0; i < stay.Nights; i++) section.Days.Add(FreeDay(stay.Destination));
                        inserted = stay.Nights;
                        warnings.Add(new(ContractWarning.Warning,
                            $"{destination}: this stay isn't in the package's program, so its {stay.Nights} day(s) were added as free days.",
                            ContractSections.Schedule));
                    }
                }
                else
                {
                    legRows.TryGetValue(step.Leg.Leg, out var legRow);
                    section.Heading = string.IsNullOrWhiteSpace(legRow?.Heading)
                        ? ContractText.SectionHeading(destination)
                        : legRow.Heading.Trim();

                    var program = package.ContractDays.Where(d => d.Leg == step.Leg.Leg).OrderBy(d => d.SortOrder).ToList();
                    var days = program.Select(d => (Day: new ContractDay { Title = d.Title, Body = d.Body }, d.IsDepartureDay)).ToList();

                    // No written-for row means nothing to measure against — keep the days as written.
                    var delta = legRow is null || !hasProgram ? 0 : stay.Nights - legRow.WrittenForNights;
                    if (delta > 0)
                    {
                        var at = days.FindIndex(d => d.IsDepartureDay);
                        if (at < 0) at = days.Count;
                        for (var i = 0; i < delta; i++) days.Insert(at, (FreeDay(stay.Destination), false));
                        inserted = delta;
                    }
                    else if (delta < 0)
                    {
                        for (var i = days.Count - 1; i >= 0 && removed < -delta; i--)
                        {
                            if (days[i].IsDepartureDay) continue;
                            days.RemoveAt(i);
                            removed++;
                        }
                        warnings.Add(new(ContractWarning.Warning,
                            $"{destination}: {removed} day(s) removed — the booking has fewer nights than the program was written for.",
                            ContractSections.Schedule));
                    }

                    section.Days.AddRange(days.Select(d => d.Day));
                }

                if (inserted > 0 || removed > 0) changedDayCount = true;
                doc.Schedule.Add(section);
                basis.Schedule.Add($"{stay.DestinationId}:{stay.Nights}:{inserted}:{removed}");
            }

            // The trip's last day closes the final section rather than opening a new heading.
            var finalDays = package.ContractDays.Where(d => d.Leg == PackageContractDay.FinalLeg).OrderBy(d => d.SortOrder).ToList();
            if (finalDays.Count > 0)
            {
                if (doc.Schedule.Count == 0) doc.Schedule.Add(new ContractSection());
                doc.Schedule[^1].Days.AddRange(finalDays.Select(d => new ContractDay { Title = d.Title, Body = d.Body }));
            }

            if (changedDayCount)
                warnings.Add(new(ContractWarning.Warning,
                    "Days were added or removed for this booking's nights. Check the night counts written in "
                    + "\"Pacote inclui\" and \"Hospedagem\" — they aren't updated automatically.",
                    ContractSections.Lists));
        }

        private static ContractDay FreeDay(Destination? destination)
        {
            var name = destination?.Name.Trim() ?? "Destino";
            return new ContractDay
            {
                Title = string.IsNullOrWhiteSpace(destination?.ContractFreeDayTitle)
                    ? ContractText.FreeDayTitle(name)
                    : destination.ContractFreeDayTitle.Trim(),
                Body = destination?.ContractFreeDayText?.Trim() ?? string.Empty,
                IsInserted = true,
            };
        }

        private sealed record AlignStep(BookingStay? Stay, ContractLegSlot? Leg);

        /// <summary>
        /// Pairs the booking's stays with the package's legs on destination, keeping both orders — a
        /// longest-common-subsequence match, so an operator-split or hand-added stay can't make a later
        /// leg (the Cairo return) swallow an earlier stay and skip the cruise in between.
        /// </summary>
        private static List<AlignStep> Align(IReadOnlyList<BookingStay> stays, IReadOnlyList<ContractLegSlot> legs)
        {
            int m = stays.Count, n = legs.Count;
            var lcs = new int[m + 1, n + 1];
            for (var i = m - 1; i >= 0; i--)
                for (var j = n - 1; j >= 0; j--)
                    lcs[i, j] = stays[i].DestinationId == legs[j].DestinationId
                        ? 1 + lcs[i + 1, j + 1]
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var steps = new List<AlignStep>();
            int si = 0, lj = 0;
            while (si < m && lj < n)
            {
                if (stays[si].DestinationId == legs[lj].DestinationId && lcs[si, lj] == 1 + lcs[si + 1, lj + 1])
                    steps.Add(new(stays[si++], legs[lj++]));
                else if (lcs[si, lj + 1] >= lcs[si + 1, lj])
                    steps.Add(new(null, legs[lj++]));
                else
                    steps.Add(new(stays[si++], null));
            }
            while (si < m) steps.Add(new(stays[si++], null));
            while (lj < n) steps.Add(new(null, legs[lj++]));
            return steps;
        }

        // ── Lists (decision 12, spec §5.4) ──────────────────────────────────────────────────────────

        private static void BuildLists(Booking booking, Package package, ContractDocument doc, ContractBasis basis, List<ContractWarning> warnings)
        {
            var items = package.ContractListItems.OrderBy(i => i.SortOrder).ToList();
            doc.Includes.AddRange(items.Where(i => i.Kind == ContractListKind.Includes).Select(i => i.Text.Trim()));
            var excludes = items.Where(i => i.Kind == ContractListKind.Excludes).ToList();

            if (booking.VisaIncluded)
            {
                doc.Excludes.AddRange(excludes.Where(i => !i.IsVisaLine).Select(i => i.Text.Trim()));
                doc.Includes.Add(ContractText.VisaLine);
                basis.Lists.Add("visa");
                if (!excludes.Any(i => i.IsVisaLine) && ContractProgramLegs.HasProgram(package))
                    warnings.Add(new(ContractWarning.Warning,
                        "The visa is included, but this package's \"Não está incluso\" list has no line marked as the visa line. "
                        + "Check that the list doesn't still exclude the visa.",
                        ContractSections.Lists));
            }
            else
            {
                doc.Excludes.AddRange(excludes.Select(i => i.Text.Trim()));
            }

            foreach (var tour in booking.Tours.OrderBy(t => t.SortOrder))
            {
                var line = ContractText.TourLine(tour.Tour?.Name.Trim() ?? "Passeio", tour.Pax);
                doc.Includes.Add(line);
                basis.Lists.Add(line);
            }
        }

        // ── Accommodation (decision 9, spec §5.5) ───────────────────────────────────────────────────

        private static void BuildAccommodation(Booking booking, ContractDocument doc, ContractBasis basis, List<ContractWarning> warnings)
        {
            var category = ContractText.Category(booking.Category);

            foreach (var group in booking.Stays.OrderBy(s => s.SortOrder).GroupBy(s => s.DestinationId))
            {
                var destination = group.First().Destination;
                var name = destination?.Name.Trim() ?? "Destino";

                if (string.IsNullOrWhiteSpace(destination?.ContractAccommodationText))
                {
                    warnings.Add(new(ContractWarning.Warning,
                        $"{name} has no accommodation text, so it isn't listed under Hospedagem. Add it in Setup ▸ Destinations.",
                        ContractSections.Accommodation));
                    continue;
                }

                var text = destination.ContractAccommodationText.Trim().Replace("{Category}", category);
                var hotels = group.Select(s => s.HotelName?.Trim())
                    .Where(h => !string.IsNullOrEmpty(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (hotels.Count > 0) text += $" ({string.Join(" / ", hotels)})";

                doc.Accommodation.Add(new ContractAccommodation { Destination = name, Text = text });
                basis.Accommodation.Add($"{name}|{text}");
            }
        }
    }
}
