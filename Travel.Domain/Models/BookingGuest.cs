using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>One named traveller on a <see cref="Booking"/>. Exactly one row is the lead guest.</summary>
    public class BookingGuest : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public string FullName { get; set; } = string.Empty;

        public GuestType Type { get; set; }

        /// <summary>Required for Child (6-12) and Infant (0-5); null for adults.</summary>
        public int? Age { get; set; }

        public bool IsLeadGuest { get; set; }

        public int SortOrder { get; set; }
    }
}
