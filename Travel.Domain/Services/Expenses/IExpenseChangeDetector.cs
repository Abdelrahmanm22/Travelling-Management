using Travel.Domain.Services.BlockServices;

namespace Travel.Domain.Services.Expenses
{
    /// <summary>
    /// Answers "have these arrival groups changed since we paid for them?" for a whole page of
    /// expenses in <b>one</b> query, by deriving each group's current fingerprint.
    ///
    /// <para>
    /// A key with no entry in the returned dictionary is a group that has emptied entirely. Its current
    /// fingerprint is therefore the empty string, which never matches a stored one — so it correctly
    /// reads "changed" rather than silently passing as unchanged.
    /// </para>
    /// </summary>
    public interface IExpenseChangeDetector
    {
        Task<IReadOnlyDictionary<BlockKey, string>> CurrentFingerprintsAsync(IEnumerable<BlockKey> keys);
    }
}
