using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    public class Destination : BaseModel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>True for a cruise / sailing stay — its <see cref="DepartureDays"/> drive booking embarkation.</summary>
        public bool IsCruise { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Sailing days — set only when <see cref="IsCruise"/> is true; null otherwise.</summary>
        public WeekDays? DepartureDays { get; set; }

        public ICollection<PackageSegment> Segments { get; set; } = new List<PackageSegment>();
    }
}
