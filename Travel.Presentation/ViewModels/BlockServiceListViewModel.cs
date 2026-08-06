using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// Filter state plus results for both arrival lists. One model serves Block Services and Single
    /// Arrivals: each view renders its own half and uses the other half's count for the cross-link,
    /// so the two pages can't disagree about what the current filter contains.
    /// </summary>
    public class BlockServiceListViewModel
    {
        [Display(Name = "Check-in from")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "Check-in to")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        [Display(Name = "Destination")]
        public int? DestinationId { get; set; }

        [Display(Name = "Status")]
        public BlockStatus? Status { get; set; }

        public IEnumerable<SelectListItem> DestinationOptions { get; set; } = Enumerable.Empty<SelectListItem>();

        /// <summary>Groups shared by two or more bookings.</summary>
        public IReadOnlyList<BlockSummary> Blocks { get; set; } = new List<BlockSummary>();

        /// <summary>Groups holding exactly one booking — still a file the supplier expects.</summary>
        public IReadOnlyList<BlockSummary> Singles { get; set; } = new List<BlockSummary>();

        /// <summary>True once the operator has submitted the filter form at least once.</summary>
        public bool Applied { get; set; }
    }
}
