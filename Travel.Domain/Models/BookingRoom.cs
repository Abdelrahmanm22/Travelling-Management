using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// Rooming for a <see cref="Booking"/> — how many rooms of each type. Operator-defined and
    /// deliberately independent of the guest count (children may share beds, supplier policies vary).
    /// </summary>
    public class BookingRoom : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public RoomType Type { get; set; }

        public int Quantity { get; set; } = 1;

        public int SortOrder { get; set; }
    }
}
