namespace Travel.Domain.Services.Expenses
{
    /// <summary>
    /// Derives every group and per-booking amount from the <b>live</b> arrival groups.
    ///
    /// <para>
    /// The one owner of both formulas, shared by the preview endpoint, Create, Edit and Recalculate, so
    /// the figure on screen and the figure that leaves the safe cannot drift. The preview is advisory
    /// only — the POST recomputes through this same service and stores that, so a tampered form can't
    /// write an arbitrary amount into a safe.
    /// </para>
    /// </summary>
    public interface IExpenseCalculator
    {
        Task<ExpenseCalculationResult> CalculateAsync(ExpenseCalculationRequest request);
    }
}
