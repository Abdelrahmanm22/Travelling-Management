using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// A booking's lifecycle. <see cref="InProgress"/> and <see cref="Confirmed"/> are owned by the
    /// Block Services workflow — a booking becomes Confirmed only once every block it belongs to is
    /// confirmed, and reverts when one is un-confirmed. <see cref="Completed"/> and
    /// <see cref="Cancelled"/> are set by an operator and are never overwritten by that recalculation.
    /// </summary>
    public enum BookingStatus
    {
        [Display(Name = "In progress")]
        InProgress = 1,

        [Display(Name = "Confirmed")]
        Confirmed = 2,

        [Display(Name = "Completed")]
        Completed = 3,

        [Display(Name = "Cancelled")]
        Cancelled = 4,
    }
}
