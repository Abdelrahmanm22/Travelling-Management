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

        /// <summary>
        /// Not a change to the booking — a voucher emailed to the client. Written by
        /// <c>BookingVoucherController</c>, never by the interceptor; the To/CC sit in its change rows.
        /// </summary>
        [Display(Name = "Voucher sent")]
        VoucherSent = 4,

        /// <summary>A voucher email that did not leave. Recorded so a colleague sees the attempt and why.</summary>
        [Display(Name = "Voucher send failed")]
        VoucherSendFailed = 5,
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
