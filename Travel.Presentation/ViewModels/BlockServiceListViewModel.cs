using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Filter state plus results for the Block Services list.</summary>
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

        public IEnumerable<SelectListItem> DestinationOptions { get; set; } = Enumerable.Empty<SelectListItem>();

        public IReadOnlyList<BlockSummary> Blocks { get; set; } = new List<BlockSummary>();

        /// <summary>True once the operator has submitted the filter form at least once.</summary>
        public bool Applied { get; set; }
    }
}
