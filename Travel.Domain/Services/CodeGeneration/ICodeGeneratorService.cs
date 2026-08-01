using Travel.Domain.Models;

namespace Travel.Domain.Services.CodeGeneration
{
    /// <summary>
    /// Generates sequential unique codes for any entity that implements <see cref="ICodedEntity"/>.
    /// The algorithm is entity-agnostic — the caller supplies the prefix (see <see cref="CodePrefixes"/>).
    ///
    /// Padding rules:
    ///   - Numbers 1 – 999,999   →  {prefix}-000001  (6-digit zero-padded)
    ///   - Numbers ≥ 1,000,000   →  {prefix}-1000000 (no padding)
    /// </summary>
    public interface ICodeGeneratorService
    {
        /// <summary>
        /// Returns the next free code for <typeparamref name="TEntity"/> by reading the newest row and
        /// incrementing its number. Read-then-write, so the returned code is only provisional until saved —
        /// uniqueness is enforced by the database, not here.
        /// </summary>
        Task<string> GenerateCodeAsync<TEntity>(string prefix)
            where TEntity : BaseModel, ICodedEntity;

        /// <summary>
        /// Adds <paramref name="entity"/> (with its whole graph) and saves it under a freshly generated code,
        /// retrying up to <paramref name="maxAttempts"/> times when a concurrent create takes the code first.
        /// Throws <see cref="Exceptions.DuplicateCodeException"/> once the attempts are exhausted.
        /// </summary>
        Task AddWithCodeAsync<TEntity>(TEntity entity, string prefix, int maxAttempts = 3)
            where TEntity : BaseModel, ICodedEntity;
    }
}
