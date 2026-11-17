namespace Travel.Domain.Services.Reports
{
    /// <summary>
    /// The revenue arithmetic shared by the Revenue report (per row and its footer) and the Home
    /// dashboard, so a margin can never read 31.4% in one place and 31.3% in the other.
    /// </summary>
    public static class RevenueFigures
    {
        /// <summary>
        /// Net as a percentage of sale, one decimal, away from zero. Null when there is no sale to
        /// divide by — "0.0%" would read as break-even, the opposite of "unknown".
        /// </summary>
        public static decimal? MarginPercent(decimal? sale, decimal? net) =>
            sale is { } s && s != 0m && net is { } n
                ? Math.Round(n / s * 100m, 1, MidpointRounding.AwayFromZero)
                : null;
    }
}
