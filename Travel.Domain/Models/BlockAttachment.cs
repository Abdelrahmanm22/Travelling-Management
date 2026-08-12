namespace Travel.Domain.Models
{
    /// <summary>
    /// A domestic flight document belonging to one arrival group. Domestic legs are arranged per
    /// (destination, check-in date) — one ticket file covers everyone flying that day — so the file is
    /// uploaded once against the group's <see cref="BlockServiceStatus"/> row rather than copied onto
    /// each booking. Every booking in the group carries it, including ones created afterwards.
    /// </summary>
    public class BlockAttachment : BaseModel
    {
        public int BlockServiceStatusId { get; set; }
        public BlockServiceStatus BlockServiceStatus { get; set; } = null!;

        /// <summary>Public "/Files/BlockAttachments/{name}" path.</summary>
        public string FileUrl { get; set; } = string.Empty;

        /// <summary>The name the operator uploaded — FileStorageService GUID-prefixes the stored name.</summary>
        public string OriginalFileName { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Snapshot of the operator's name, no FK — same reasoning as
        /// <see cref="BlockServiceStatus.UpdatedByName"/>: it must survive the user being deleted.
        /// </summary>
        public string? UploadedByName { get; set; }
    }
}
