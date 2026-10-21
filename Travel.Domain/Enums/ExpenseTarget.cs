using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>
    /// What an expense is charged against — chosen once, on Create, and fixed afterwards like the safe
    /// and the type. One expense targets one kind; it never mixes arrival groups and bookings.
    /// </summary>
    public enum ExpenseTarget
    {
        /// <summary>A supplier cost for an arrival (a hotel block) — split down to the bookings in each group.</summary>
        [Display(Name = "Arrival groups")]
        ArrivalGroups = 1,

        /// <summary>A cost that belongs to the trip itself (a transfer, a guide, a cancellation fee).</summary>
        [Display(Name = "Bookings")]
        Bookings = 2,
    }
}
