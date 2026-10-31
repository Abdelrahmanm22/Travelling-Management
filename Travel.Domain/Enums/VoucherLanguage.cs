using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>The language a booking voucher (PDF and email) is written in — the operator picks it per send.</summary>
    public enum VoucherLanguage
    {
        [Display(Name = "English")]
        English = 1,

        [Display(Name = "Portuguese")]
        Portuguese = 2,
    }
}
