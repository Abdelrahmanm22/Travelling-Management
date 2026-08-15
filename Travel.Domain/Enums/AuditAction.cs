using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum AuditAction
    {
        [Display(Name = "Created")]
        Created = 1,

        [Display(Name = "Updated")]
        Updated = 2,

        [Display(Name = "Deleted")]
        Deleted = 3,
    }

    /// <summary>What happened to one audited row.</summary>
    public enum AuditChangeKind
    {
        [Display(Name = "Added")]
        Added = 1,

        [Display(Name = "Changed")]
        Modified = 2,

        [Display(Name = "Removed")]
        Removed = 3,
    }
}
