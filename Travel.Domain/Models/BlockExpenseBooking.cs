namespace Travel.Domain.Models
{
    /// <summary>
    /// One booking's share of an expense, frozen at the figure that was actually paid.
    /// </summary>
    public class BlockExpenseBooking : BaseModel
    {
        public int ExpenseGroupId { get; set; }
        public BlockExpenseGroup ExpenseGroup { get; set; } = null!;

        /// <summary>
        /// Nullable, and the foreign key is <b>NoAction</b> — a cascading FK into Booking would be a
        /// second cascade path into a booking-owned graph (SQL Server error 1785), the same break the
        /// block confirmation snapshot already documents.
        ///
        /// <para>
        /// <c>BookingController.Delete</c> therefore clears it by hand — and <b>NULLs it rather than
        /// deleting the row</b>. This line records money that already left a safe; removing it would
        /// make its group's stored Amount stop equalling the sum of its own visible lines, with nothing
        /// anywhere to say why. <see cref="BookingCode"/> keeps the line readable afterwards, exactly
        /// as CreatedByName does on a transaction.
        /// </para>
        /// </summary>
        public int? BookingId { get; set; }
        public Booking? Booking { get; set; }

        public string BookingCode { get; set; } = null!;

        public string LeadGuestName { get; set; } = null!;

        public decimal Amount { get; set; }

        /// <summary>The stay nights this allocation multiplied by — the per-room driver.</summary>
        public int Nights { get; set; }

        /// <summary>The passenger count this allocation divided by — the per-pax driver.</summary>
        public int EligiblePax { get; set; }

        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        public string RoomsFingerprint { get; set; } = string.Empty;
    }
}
