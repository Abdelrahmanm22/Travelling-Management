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

        /// <summary>
        /// The supplier confirmed this group, but it has changed since — a booking joined or left, or
        /// one of them changed its rooms, PAX or nights. Derived at read time from the confirmation
        /// snapshot (<c>BlockConfirmedBooking</c>) and <b>never stored or set by hand</b>: the only way
        /// out is to re-request the block and confirm it again, which rewrites the snapshot.
        /// </summary>
        [Display(Name = "Needs amendment")]
        NeedsAmendment = 4,
    }
}
