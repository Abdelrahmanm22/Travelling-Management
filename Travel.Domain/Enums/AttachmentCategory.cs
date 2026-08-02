using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum AttachmentCategory
    {
        [Display(Name = "Flight")]
        Flight = 1,

        [Display(Name = "Passport")]
        Passport = 2,

        [Display(Name = "Domestic flight")]
        DomesticFlight = 3,

        [Display(Name = "Contract")]
        Contract = 4,
    }
}
