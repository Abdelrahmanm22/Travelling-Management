using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// A kind of cost the company pays on an arrival group — "Hotel", "Guide", "Transfer" — carrying
    /// the rule for <i>how</i> it divides across the group. The rates themselves are never stored here:
    /// they are typed each time the type is applied, because they change per supplier and per season.
    /// </summary>
    public class ExpenseType : BaseModel
    {
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public ExpenseBasis Basis { get; set; } = ExpenseBasis.PerRoom;

        /// <summary>
        /// Read only when <see cref="Basis"/> is <see cref="ExpenseBasis.PerPax"/>. Both flags are
        /// forced back to false on every save of a per-room type, so flipping a type to per-pax later
        /// can't resurrect a setting nobody re-chose — the same call the booking form makes when it
        /// nulls a price sitting behind a zero count.
        /// </summary>
        public bool IncludeChildren { get; set; }
        public bool IncludeInfants { get; set; }

        /// <summary>The soft retire — an inactive type stays on the expenses that already used it.</summary>
        public bool IsActive { get; set; } = true;
    }
}
