namespace Travel.Domain.Models
{
    /// <summary>A receipt or invoice proving one safe movement. Same shape as TransactionAttachment.</summary>
    public class SafeEntryAttachment : BaseModel
    {
        public int EntryId { get; set; }
        public SafeEntry Entry { get; set; } = null!;

        public string FileUrl { get; set; } = null!;

        public string OriginalFileName { get; set; } = null!;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public string? UploadedByName { get; set; }
    }
}
