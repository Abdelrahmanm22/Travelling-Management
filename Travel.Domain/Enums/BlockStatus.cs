using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// Where one arrival group — a (destination, check-in date) pair — stands with its supplier.
    /// A group with no stored status row reads as <see cref="NotBooked"/>.
    /// </summary>
    public enum BlockStatus
    {
        [Display(Name = "Not booked")]
        NotBooked = 1,

        [Display(Name = "In progress")]
        InProgress = 2,

        [Display(Name = "Confirmed")]
        Confirmed = 3,
    }
}
