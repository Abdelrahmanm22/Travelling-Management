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
        /// What the supplier was told about this group no longer describes it — a booking joined or
        /// left, or one of them changed its rooms, PAX or nights. Reached from either
        /// <see cref="InProgress"/> (the booking request we sent is out of date) or
        /// <see cref="Confirmed"/> (the confirmation we got back is), which is why the badge is the
        /// same and the wording beside it differs.
        ///
        /// <para>
        /// Derived at read time from <c>BlockRecordedBooking</c> and <b>never stored or set by hand</b>:
        /// the only way out is to re-save the status the block already has, which re-records the group
        /// as it now stands.
        /// </para>
        /// </summary>
        [Display(Name = "Needs amendment")]
        NeedsAmendment = 4,
    }
}
