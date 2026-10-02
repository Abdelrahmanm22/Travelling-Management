using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One room type's extra-night rate on a <see cref="BookingStay"/> — <b>per room, per night</b>,
    /// with the number of rooms of that type the guest actually keeps for the extra nights.
    ///
    /// <para>
    /// A row per type rather than six nullable columns on the stay, for the same reasons
    /// <see cref="BlockExpenseRate"/> gives: a fourth <see cref="RoomType"/> needs no migration, and
    /// the type stays the key it already is on <see cref="BookingRoom"/> and in
    /// <c>BlockSnapshot.RoomsFingerprint</c>.
    /// </para>
    ///
    /// <para>
    /// <see cref="Quantity"/> is seeded from the booking's <see cref="Booking.Rooms"/> tally but is
    /// deliberately its own figure: only some of the rooms commonly stay on for the extra nights.
    /// </para>
    /// </summary>
    public class BookingStayRate : BaseModel
    {
        public int StayId { get; set; }
        public BookingStay Stay { get; set; } = null!;

        public RoomType RoomType { get; set; }

        /// <summary>Rooms of this type kept for the extra nights. Rows at 0 are not stored.</summary>
        public int Quantity { get; set; }

        public decimal Rate { get; set; }
    }
}
