using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// A supporting document attached to a <see cref="Booking"/> — a stored file for the
    /// Flight/Passport/DomesticFlight categories, or a link for the Contract category.
    /// </summary>
    public class BookingAttachment : BaseModel
    {
        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public AttachmentCategory Category { get; set; }

        /// <summary>Public "/Files/BookingAttachments/{name}" path — null for the Contract row.</summary>
        public string? FileUrl { get; set; }

        /// <summary>The name the operator uploaded — FileStorageService GUID-prefixes the stored name.</summary>
        public string? OriginalFileName { get; set; }

        /// <summary>Populated only for Category = Contract.</summary>
        public string? LinkUrl { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
