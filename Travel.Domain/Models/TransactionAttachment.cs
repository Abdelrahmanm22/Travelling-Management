namespace Travel.Domain.Models
{
    /// <summary>
    /// A receipt or proof-of-transfer file on a <see cref="BookingTransaction"/>. Same shape as
    /// <see cref="BlockAttachment"/>: the row is owned by its parent, but the file on disk is not —
    /// the cascade removes rows only, so every delete path must call IFileStorageService itself.
    /// </summary>
    public class TransactionAttachment : BaseModel
    {
        public int TransactionId { get; set; }
        public BookingTransaction Transaction { get; set; } = null!;

        public string FileUrl { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
