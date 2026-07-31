using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.Itinerary;

namespace Travel.Services
{
    /// <summary>
    /// Builds a booking's night-by-night schedule. Embarkation is driven entirely by the cruise
    /// destination's configured departure days — no company-specific rules live in this code.
    /// </summary>
    public class ItineraryGeneratorService : IItineraryGeneratorService
    {
        public IReadOnlyList<EmbarkOption> GetEmbarkOptions(Package package, DateOnly arrival)
        {
            var segments = package.Segments.OrderBy(s => s.SortOrder).ToList();
            if (!segments.Any(s => s.Destination.IsCruise))
                return Array.Empty<EmbarkOption>();

            var middleNights = segments.Sum(s => s.Nights);
            var baseTotal = (package.TotalNights ?? 0) - middleNights;

            return BuildEmbarkOptions(package, arrival, segments, baseTotal);
        }

        public GeneratedItinerary Generate(Package package, DateOnly arrival, WeekDays? embarkPreference, DateOnly? departure)
        {
            var segments = package.Segments.OrderBy(s => s.SortOrder).ToList();
            var middleNights = segments.Sum(s => s.Nights);

            int total = package.TotalNights
                ?? (departure is null
                        ? throw new InvalidOperationException("A departure date is required for variable-length packages.")
                        : departure.Value.DayNumber - arrival.DayNumber);

            if (total < 1)
                throw new InvalidOperationException("Total nights must be at least 1.");

            var baseTotal = total - middleNights;
            if (baseTotal < 0)
                throw new InvalidOperationException("Segment nights exceed the package total.");

            var hasCruise = segments.Any(s => s.Destination.IsCruise);

            var result = new GeneratedItinerary();
            var stays = result.Stays;
            var order = 0;
            int leadNights, returnNights;

            if (hasCruise)
            {
                var options = BuildEmbarkOptions(package, arrival, segments, baseTotal);
                var chosen = options.FirstOrDefault(o => embarkPreference.HasValue && o.Day == embarkPreference.Value)
                             ?? options[0];

                leadNights = chosen.LeadNights;

                var computedReturn = baseTotal - leadNights;
                if (computedReturn < package.BaseReturnNights)
                {
                    result.ExtraNights = package.BaseReturnNights - computedReturn;
                    returnNights = package.BaseReturnNights;
                    result.ExtensionReason =
                        $"{arrival.DayOfWeek} arrival — the cruise sails {chosen.Day} ({chosen.Date:dd MMM}); " +
                        $"extra {package.BaseDestination.Name} night(s) required for the return flight.";
                }
                else
                {
                    returnNights = computedReturn;
                }
            }
            else if (segments.Count == 0)
            {
                // Base-only package (e.g. Cairo-only): a single base stay for the whole trip.
                leadNights = total;
                returnNights = 0;
            }
            else
            {
                // No weekday constraint: run segments in order, keep the final nights at base.
                returnNights = Math.Min(package.BaseReturnNights, baseTotal);
                leadNights = baseTotal - returnNights;
            }

            var cursor = arrival;

            // Base lead.
            if (leadNights > 0)
            {
                stays.Add(BaseStay(package, cursor, leadNights, order++));
                cursor = cursor.AddDays(leadNights);
            }

            // Middle segments in order.
            foreach (var seg in segments)
            {
                stays.Add(new GeneratedStay
                {
                    DestinationId = seg.DestinationId,
                    IsCruise = seg.Destination.IsCruise,
                    CheckIn = cursor,
                    Nights = seg.Nights,
                    SortOrder = order++,
                });
                cursor = cursor.AddDays(seg.Nights);
            }

            // Base return (guaranteed final leg at base).
            if (returnNights > 0)
            {
                stays.Add(BaseStay(package, cursor, returnNights, order++));
                cursor = cursor.AddDays(returnNights);
            }

            result.DepartureDate = arrival.AddDays(total + result.ExtraNights);

            var scheduled = stays.Sum(s => s.Nights);
            if (scheduled != total + result.ExtraNights)
                throw new InvalidOperationException(
                    $"Generated schedule is {scheduled} nights but expected {total + result.ExtraNights}.");

            return result;
        }

        /// <summary>
        /// One candidate per configured departure day: the first sailing that still leaves at least one night at
        /// the base first (arriving on a sailing day therefore rolls to the following week).
        /// </summary>
        private static List<EmbarkOption> BuildEmbarkOptions(
            Package package, DateOnly arrival, List<PackageSegment> segments, int baseTotal)
        {
            var cruiseIndex = segments.FindIndex(s => s.Destination.IsCruise);
            var cruise = segments[cruiseIndex];

            // Stays scheduled between the base lead and the cruise (usually none).
            var nightsBeforeCruise = segments.Take(cruiseIndex).Sum(s => s.Nights);

            var sailingDays = cruise.Destination.DepartureDays;
            if (sailingDays is null || (int)sailingDays.Value == 0)
                throw new InvalidOperationException(
                    $"\"{cruise.Destination.Name}\" has no departure days configured. Set them on the destination first.");

            // Earliest date the cruise could start while keeping a night at the base.
            var earliest = arrival.AddDays(nightsBeforeCruise + 1);

            var options = new List<EmbarkOption>();
            foreach (var day in Enum.GetValues<WeekDays>())
            {
                if (!sailingDays.Value.HasFlag(day))
                    continue;

                var offset = (((int)ToDayOfWeek(day) - (int)earliest.DayOfWeek) + 7) % 7;
                var date = earliest.AddDays(offset);
                var leadNights = (date.DayNumber - arrival.DayNumber) - nightsBeforeCruise;

                options.Add(new EmbarkOption
                {
                    Day = day,
                    Date = date,
                    LeadNights = leadNights,
                    ExtraNights = Math.Max(0, package.BaseReturnNights - (baseTotal - leadNights)),
                });
            }

            return options.OrderBy(o => o.Date).ToList();
        }

        private static GeneratedStay BaseStay(Package package, DateOnly checkIn, int nights, int order) => new()
        {
            DestinationId = package.BaseDestinationId,
            IsCruise = false,
            CheckIn = checkIn,
            Nights = nights,
            SortOrder = order,
        };

        private static DayOfWeek ToDayOfWeek(WeekDays day) => day switch
        {
            WeekDays.Sunday => DayOfWeek.Sunday,
            WeekDays.Monday => DayOfWeek.Monday,
            WeekDays.Tuesday => DayOfWeek.Tuesday,
            WeekDays.Wednesday => DayOfWeek.Wednesday,
            WeekDays.Thursday => DayOfWeek.Thursday,
            WeekDays.Friday => DayOfWeek.Friday,
            WeekDays.Saturday => DayOfWeek.Saturday,
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Departure day must be a single weekday."),
        };
    }
}
