namespace Travel.Domain.Models
{
    /// <summary>
    /// One ticket file of a <see cref="BlockFlight"/>. A flight can carry several (the airline often
    /// sends one e-ticket per passenger); the flight facts and the bookings live on the flight.
    /// </summary>
    public class BlockAttachment : BaseModel
    {
        public int BlockFlightId { get; set; }
        public BlockFlight BlockFlight { get; set; } = null!;

        /// <summary>Public "/Files/BlockFlights/{name}" path.</summary>
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
