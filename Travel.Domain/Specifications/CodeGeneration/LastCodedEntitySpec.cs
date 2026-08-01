using Travel.Domain.Models;

namespace Travel.Domain.Specifications.CodeGeneration
{
    /// <summary>
    /// The newest row of a coded entity — paired with <c>GetByIdAsync</c> (a FirstOrDefault) this is a
    /// SELECT TOP 1 … ORDER BY Id DESC, i.e. ONE row, not the whole table.
    ///
    /// Ordered by <c>Id</c>, never by <c>Code</c>: codes are issued monotonically against the IDENTITY PK,
    /// so the newest row holds the highest number. Ordering by the Code *string* breaks at the padding
    /// boundary — "MUSB-1000000" sorts before "MUSB-999999".
    ///
    /// ⚠ SOFT DELETE: this project has no global query filters today. The moment a HasQueryFilter is added
    /// to a coded entity (Booking is planned), this spec MUST also ignore query filters — a soft-deleted row
    /// still owns its Code in the unfiltered unique index, so an invisible last row makes the generator
    /// re-issue a taken code. That needs an IgnoreQueryFilters flag on ISpecifications/SpecificationEvalutor.
    /// </summary>
    public class LastCodedEntitySpec<TEntity> : BaseSpecifications<TEntity>
        where TEntity : BaseModel, ICodedEntity
    {
        public LastCodedEntitySpec()
        {
            SetOrderByDesc(x => x.Id);
        }
    }
}
