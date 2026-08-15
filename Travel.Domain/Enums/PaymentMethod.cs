using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum PaymentMethod
    {
        [Display(Name = "Cash")]
        Cash = 1,

        [Display(Name = "Bank transfer")]
        BankTransfer = 2,

        [Display(Name = "Credit card")]
        CreditCard = 3,
    }
}
