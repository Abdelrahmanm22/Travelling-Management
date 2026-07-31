namespace Travel.Domain.Enums
{
    /// <summary>
    /// Days of the week as a bitmask — combine values for entities that recur on multiple days
    /// (e.g. a cruise's departure days: Wednesday | Friday).
    /// </summary>
    [Flags]
    public enum WeekDays
    {
        Sunday = 1,
        Monday = 2,
        Tuesday = 4,
        Wednesday = 8,
        Thursday = 16,
        Friday = 32,
        Saturday = 64
    }
}
