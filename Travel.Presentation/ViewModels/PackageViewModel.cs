using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Travel.Presentation.ViewModels
{
    public class PackageViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Required(ErrorMessage = "Base destination is required.")]
        [Display(Name = "Base destination")]
        public int BaseDestinationId { get; set; }

        [Range(1, 120, ErrorMessage = "Total nights must be between 1 and 120.")]
        [Display(Name = "Total nights (leave empty for variable-length)")]
        public int? TotalNights { get; set; }

        [Range(1, 60, ErrorMessage = "Return nights must be at least 1.")]
        [Display(Name = "Base return nights")]
        public int BaseReturnNights { get; set; } = 1;

        [Display(Name = "Cover image")]
        public IFormFile? CoverImage { get; set; }

        public string? ExistingImageUrl { get; set; }

        public List<PackageSegmentInput> Segments { get; set; } = new();

        // Dropdown sources — populated by the controller, not model-bound.
        public IEnumerable<SelectListItem> BaseDestinationOptions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem> SegmentDestinationOptions { get; set; } = new List<SelectListItem>();
    }

    public class PackageSegmentInput
    {
        [Required(ErrorMessage = "Destination is required.")]
        public int DestinationId { get; set; }

        [Range(1, 60, ErrorMessage = "Nights must be between 1 and 60.")]
        public int Nights { get; set; } = 1;

        public int SortOrder { get; set; }
    }
}
