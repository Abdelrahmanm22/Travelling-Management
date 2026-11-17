namespace Travel.Domain.Services.Dashboard
{
    /// <summary>
    /// The Home dashboard's figures, one method per section. Each section is gated by its own
    /// <c>Dashboard.*</c> permission and the controller calls <b>only</b> the permitted methods, so a
    /// section the user can't see costs no queries and its numbers never reach the page.
    ///
    /// <para>
    /// <paramref name="today"/> is passed in rather than read here, so "today" is decided once per
    /// request, the same way for every section.
    /// </para>
    /// </summary>
    public interface IDashboardQuery
    {
        Task<OperationsStats> GetOperationsAsync(DateOnly today, DateOnly from, DateOnly to);

        Task<SalesStats> GetSalesAsync(DateOnly today, DateOnly from, DateOnly to);

        Task<PaymentsStats> GetPaymentsAsync(DateOnly today, DateOnly from, DateOnly to);

        Task<SafesStats> GetSafesAsync(DateOnly from, DateOnly to);
    }
}
