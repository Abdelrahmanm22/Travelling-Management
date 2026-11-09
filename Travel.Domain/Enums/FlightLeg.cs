namespace Travel.Domain.Enums
{
    /// <summary>
    /// Which of a booking's flights a <see cref="Models.MovementType"/> meets. One enum rather than
    /// three flags, because the flight resolver is a single switch on it. Null on the type means the
    /// movement isn't a flight at all (a day tour).
    /// </summary>
    public enum FlightLeg
    {
        InternationalArrival = 1,
        InternationalDeparture = 2,
        DomesticArrival = 3,
        DomesticDeparture = 4,
    }

    public static class FlightLegs
    {
        public static bool IsArrival(this FlightLeg leg) =>
            leg is FlightLeg.InternationalArrival or FlightLeg.DomesticArrival;

        public static bool IsDomestic(this FlightLeg leg) =>
            leg is FlightLeg.DomesticArrival or FlightLeg.DomesticDeparture;

        public static string Label(this FlightLeg leg) => leg switch
        {
            FlightLeg.InternationalArrival => "International arrival",
            FlightLeg.InternationalDeparture => "International departure",
            FlightLeg.DomesticArrival => "Domestic arrival",
            FlightLeg.DomesticDeparture => "Domestic departure",
            _ => leg.ToString(),
        };
    }
}
