namespace Travel.Domain.Models
{
    /// <summary>
    /// A travel package / itinerary definition — a base destination (where guests arrive/depart) with a
    /// fixed lead stay, followed by an ordered list of middle segments. The return-to-base for the
    /// remaining nights is implied by the base and resolved later by the accommodation generator.
    /// </summary>
    public class Package : BaseModel, ICodedEntity
    {
        /// <summary>Auto-generated sequential reference, e.g. "MUSP-000001". Issued on create, never changed.</summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Cover image path, e.g. "/Files/Packages/{name}"; null when none uploaded.</summary>
        public string? ImageUrl { get; set; }

        public int BaseDestinationId { get; set; }
        public Destination BaseDestination { get; set; } = null!;

        /// <summary>
        /// Fixed total nights for the package (e.g. 7/9/10). Null = variable-length: the operator enters the
        /// departure date per booking and the base nights fill the range. Cruise packages must set this.
        /// </summary>
        public int? TotalNights { get; set; }

        /// <summary>Nights the guest always spends back at the base on the final leg (hard rule, minimum 1).</summary>
        public int BaseReturnNights { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PackageSegment> Segments { get; set; } = new List<PackageSegment>();
    }
}
