using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum AttachmentCategory
    {
        [Display(Name = "Flight")]
        Flight = 1,

        [Display(Name = "Passport")]
        Passport = 2,

        // 3 was DomesticFlight. Those documents belong to the arrival group, not the booking — see
        // BlockAttachment — and the rows were deleted by the migration that moved them. Do not reuse 3.

        [Display(Name = "Contract")]
        Contract = 4,
    }
}
