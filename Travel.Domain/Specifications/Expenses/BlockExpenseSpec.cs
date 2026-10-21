using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Expenses
{
    /// <summary>
    /// A block expense with everything any view of it reads.
    ///
    /// <para>
    /// 🔴 Every constructor shares <see cref="AddIncludes"/> deliberately. Two specifications over the
    /// same rows at different depths is exactly the bug that made a booking's Details page print "—"
    /// for a receipt the transactions list showed correctly: <b>an include must reach as deep as the
    /// view reads</b>, and the surest way to keep that true is for there to be only one include set.
    /// </para>
    /// </summary>
    public class BlockExpenseSpec : BaseSpecifications<BlockExpense>
    {
        public BlockExpenseSpec(int id) : base(e => e.Id == id)
        {
            AddIncludes();
        }

        public BlockExpenseSpec(
            DateOnly? from, DateOnly? to, int? safeId, int? expenseTypeId, int? destinationId,
            ExpenseTarget? target = null)
            : base(e => (!from.HasValue || e.SpentOn >= from.Value)
                     && (!to.HasValue || e.SpentOn <= to.Value)
                     && (!safeId.HasValue || (e.SafeEntry != null && e.SafeEntry.SafeId == safeId.Value))
                     && (!expenseTypeId.HasValue || e.ExpenseTypeId == expenseTypeId.Value)
                     && (!target.HasValue || e.Target == target.Value)
                     // A booking expense has no groups, so it matches a destination through the
                     // itinerary of the bookings it was applied to — otherwise the filter hides it.
                     && (!destinationId.HasValue
                         || e.Groups.Any(g => g.DestinationId == destinationId.Value)
                         || e.DirectBookings.Any(l => l.Booking != null
                                                   && l.Booking.Stays.Any(s => s.DestinationId == destinationId.Value))))
        {
            AddIncludes();
            SetOrderByDesc(e => e.SpentOn);
        }

        /// <summary>Every expense touching one arrival group — the card on the block's Details page.</summary>
        public BlockExpenseSpec(int destinationId, DateOnly checkIn, PackageCategory category, int? privateBookingId)
            : base(e => e.Groups.Any(g => g.DestinationId == destinationId
                                       && g.CheckInDate == checkIn
                                       && g.Category == category
                                       && g.PrivateBookingId == privateBookingId))
        {
            AddIncludes();
            SetOrderByDesc(e => e.SpentOn);
        }

        /// <summary>
        /// Every expense touching one booking — applied to it directly, or to an arrival group it
        /// was part of. The Expenses card on <c>Booking/Details</c> lists both, in one query.
        /// </summary>
        public static BlockExpenseSpec ForBooking(int bookingId) => new(bookingId, forBooking: true);

        private BlockExpenseSpec(int bookingId, bool forBooking)
            : base(e => e.DirectBookings.Any(l => l.BookingId == bookingId)
                     || e.Groups.Any(g => g.Bookings.Any(b => b.BookingId == bookingId)))
        {
            AddIncludes();
            SetOrderByDesc(e => e.SpentOn);
        }

        /// <summary>Whether any expense still uses a type — the delete guard on the catalogue.</summary>
        public static BaseSpecifications<BlockExpense> UsingType(int expenseTypeId) =>
            new(e => e.ExpenseTypeId == expenseTypeId);

        private void AddIncludes()
        {
            AddInclude("ExpenseType");
            AddInclude("Rates");
            AddInclude("Groups");
            AddInclude("Groups.Destination");
            AddInclude("Groups.Bookings");
            AddInclude("DirectBookings");
            AddInclude("SafeEntry");
            AddInclude("SafeEntry.Safe");
            AddInclude("SafeEntry.Attachments");
        }
    }
}
