namespace Travel.Domain.Models
{
    /// <summary>A single ordered middle stay within a <see cref="Package"/> — a destination and its fixed nights.</summary>
    public class PackageSegment : BaseModel
    {
        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        public int DestinationId { get; set; }
        public Destination Destination { get; set; } = null!;

        public int Nights { get; set; }

        /// <summary>Zero-based position of this segment within the package.</summary>
        public int SortOrder { get; set; }
    }
}
