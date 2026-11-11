namespace Travel.Domain.Services.InternalMovements
{
    /// <summary>
    /// Reads for Internal Movements — the calendar, the day board, the report and the save check — all
    /// through <see cref="MovementPresence"/>, so each surface derives presence, flights, hotels and
    /// "needs review" the same way. Every call costs a fixed number of queries, never one per day or row.
    /// </summary>
    public interface IInternalMovementQuery
    {
        /// <summary>One entry per day of the month that starts at <paramref name="monthStart"/>.</summary>
        Task<IReadOnlyList<CalendarDay>> GetMonthAsync(int destinationId, string destinationName, DateOnly monthStart);

        Task<DayBoard> GetDayAsync(int destinationId, string destinationName, DateOnly date);

        /// <summary>Report rows, ordered by day, then pickup time.</summary>
        Task<IReadOnlyList<MovementLine>> GetReportLinesAsync(DateOnly? from, DateOnly? to, int? destinationId, int? driverId);

        /// <summary>Which of these bookings may receive a movement at the destination on the day (§5.1, not cancelled).</summary>
        Task<QualificationResult> QualifyAsync(int destinationId, DateOnly date, IReadOnlyCollection<int> bookingIds);
    }
}
