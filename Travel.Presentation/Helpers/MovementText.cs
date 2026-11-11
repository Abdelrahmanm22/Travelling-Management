using System.Globalization;
using Travel.Domain.Enums;
using Travel.Domain.Services.InternalMovements;

namespace Travel.Presentation.Helpers
{
    /// <summary>
    /// The on-screen and in-sheet spelling of Internal Movements facts, so the board, the report and the
    /// `.xlsx` can't disagree (the <see cref="StatusBadges"/> role).
    /// </summary>
    public static class MovementText
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Time(TimeOnly time) => time.ToString("HH:mm", Invariant);

        public static string Time(TimeOnly? time) => time is { } t ? Time(t) : "—";

        public static string Date(DateOnly date) => date.ToString("dd MMM", Invariant);

        public static string LongDate(DateOnly date) => date.ToString("dddd dd MMM yyyy", Invariant);

        /// <summary>The passive hint on a booking card: "Arriving today 23:40 · MS 986".</summary>
        public static string Hint(FlightHint hint)
        {
            var verb = hint.Leg switch
            {
                FlightLeg.InternationalArrival => "Arriving today",
                FlightLeg.InternationalDeparture => "Departing today",
                FlightLeg.DomesticArrival => "Domestic arrival today",
                _ => "Domestic departure today",
            };

            if (!hint.Flight.IsRecorded)
                return $"{verb} — flight not recorded";

            var parts = new List<string> { verb };
            if (hint.Flight.Time is { } t) parts[0] += " " + Time(t);
            if (!string.IsNullOrWhiteSpace(hint.Flight.Number)) parts.Add(hint.Flight.Number!.Trim());
            return string.Join(" · ", parts);
        }

        public static string HintIcon(FlightLeg leg) => leg.IsArrival() ? "bxs-plane-land" : "bxs-plane-take-off";

        public static string FlightNumber(MovementLine line) =>
            line.FlightLeg is null ? "—" : string.IsNullOrWhiteSpace(line.Flight?.Number) ? "—" : line.Flight!.Number!.Trim();

        public static string FlightTime(MovementLine line) =>
            line.FlightLeg is null ? "—" : Time(line.Flight?.Time);

        /// <summary>The report's Status column: the review reason first, then the amber warning, else blank.</summary>
        public static string Status(MovementLine line) =>
            line.ReviewReason is { } reason ? $"Needs review: {reason}"
            : line.NoFlightRecorded ? "No flight recorded"
            : string.Empty;
    }
}
