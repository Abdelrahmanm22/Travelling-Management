using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// Which way the money moved. <see cref="Models.BookingTransaction.Amount"/> is always positive —
    /// this carries the sign, so a list never asks the reader to spot a minus sign.
    /// </summary>
    public enum TransactionType
    {
        [Display(Name = "Payment")]
        Payment = 1,

        [Display(Name = "Refund")]
        Refund = 2,
    }
}
