using Travel.Domain.Models;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>One leg of a package's itinerary, as the contract program sees it.</summary>
    public sealed record ContractLegSlot(int Leg, int DestinationId, string DestinationName, string Label, int DefaultNights);

    /// <summary>
    /// The legs a package's generated itinerary can have, in order — the one owner of what a leg index
    /// means, shared by the Setup editor and the builder.
    ///
    /// <para>
    /// <c>ItineraryGeneratorService</c> emits Lead (base) → one stay per segment → Return (base), and
    /// skips a leg with 0 nights. A package with no segments is a single base stay, so it has no Return
    /// leg at all.
    /// </para>
    /// </summary>
    public static class ContractProgramLegs
    {
        /// <param name="package">Loaded with <c>BaseDestination</c> and <c>Segments.Destination</c>.</param>
        public static IReadOnlyList<ContractLegSlot> For(Package package)
        {
            var segments = package.Segments.OrderBy(s => s.SortOrder).ToList();
            var baseName = package.BaseDestination?.Name.Trim() ?? "Base";

            // The generator's own split, before any per-booking cruise or Saturday adjustment — only a
            // starting value for WrittenForNights, which the operator can change.
            var baseTotal = package.TotalNights is { } total ? Math.Max(0, total - segments.Sum(s => s.Nights)) : 1;
            var returnNights = segments.Count == 0 ? 0 : Math.Min(package.BaseReturnNights, baseTotal);
            var leadNights = Math.Max(0, baseTotal - returnNights);

            var legs = new List<ContractLegSlot>
            {
                new(0, package.BaseDestinationId, baseName, baseName, leadNights),
            };
            for (var i = 0; i < segments.Count; i++)
            {
                var name = segments[i].Destination?.Name.Trim() ?? "Segment";
                legs.Add(new(i + 1, segments[i].DestinationId, name, name, segments[i].Nights));
            }
            if (segments.Count > 0)
                legs.Add(new(segments.Count + 1, package.BaseDestinationId, baseName, $"{baseName} (retorno)", returnNights));

            return legs;
        }

        /// <summary>
        /// True when the saved program was written for a different leg structure — a segment added,
        /// removed or re-pointed since. Leg indexes would then attach days to the wrong destination.
        /// </summary>
        public static bool IsOutOfDate(Package package, IReadOnlyList<ContractLegSlot> legs)
        {
            var byLeg = legs.ToDictionary(l => l.Leg);
            if (package.ContractLegs.Any(r => !byLeg.TryGetValue(r.Leg, out var slot) || slot.DestinationId != r.DestinationId))
                return true;
            return package.ContractDays.Any(d => d.Leg != PackageContractDay.FinalLeg && !byLeg.ContainsKey(d.Leg));
        }

        public static bool HasProgram(Package package) =>
            package.ContractDays.Count > 0 || package.ContractListItems.Count > 0;
    }
}
