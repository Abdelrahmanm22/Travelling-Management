using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>Where a <c>ContractTermsSection</c> prints in the contract.</summary>
    public enum ContractTermsPlacement
    {
        /// <summary>The title's party paragraph, before the passengers.</summary>
        [Display(Name = "Opening")]
        Opening = 1,

        /// <summary>Clause 1.0, before the schedule.</summary>
        [Display(Name = "Object clause")]
        ObjectClause = 2,

        /// <summary>1.3 → 7 and the double-room clause, after Documentos Requeridos.</summary>
        [Display(Name = "Closing clauses")]
        Closing = 3,
    }
}
