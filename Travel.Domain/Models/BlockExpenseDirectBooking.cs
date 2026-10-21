namespace Travel.Domain.Models
{
    /// <summary>
    /// One booking's share of an expense applied to <b>bookings</b> rather than arrival groups
    /// (<see cref="BlockExpense.Target"/> = <c>Bookings</c>), frozen at the figure that was paid.
    ///
    /// <para>
    /// A sibling of <see cref="BlockExpenseBooking"/>, not a reuse of it: that line hangs off a
    /// <see cref="BlockExpenseGroup"/>, which is a destination + check-in. Giving it a second, direct
    /// FK to the expense would be a second cascade path from <c>BlockExpenses</c> (SQL Server 1785).
    /// </para>
    /// </summary>
    public class BlockExpenseDirectBooking : BaseModel
    {
        public int ExpenseId { get; set; }
        public BlockExpense Expense { get; set; } = null!;

        /// <summary>
        /// Nullable and NoAction, exactly like <see cref="BlockExpenseBooking.BookingId"/>:
        /// <c>BookingController.Delete</c> NULLs it rather than deleting the row, because the line
        /// records money that already left a safe.
        /// </summary>
        public int? BookingId { get; set; }
        public Booking? Booking { get; set; }

        public string BookingCode { get; set; } = null!;

        public string LeadGuestName { get; set; } = null!;

        public decimal Amount { get; set; }

        /// <summary>The booking's whole-trip nights (Σ stays) — the per-room multiplier.</summary>
        public int Nights { get; set; }

        /// <summary>The passenger count this allocation divided by — the per-pax driver.</summary>
        public int EligiblePax { get; set; }

        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        public string RoomsFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="Services.Expenses.ExpenseFingerprint.ForBooking"/> at the moment of paying —
        /// compared against the live booking to raise "Needs recalculating".
        /// </summary>
        public string AppliedFingerprint { get; set; } = string.Empty;
    }
}
