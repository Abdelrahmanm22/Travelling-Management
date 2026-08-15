using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Presentation.ViewModels
{
    public class TourViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(150)]
        [Remote(action: "IsNameAvailable", controller: "Tour", AdditionalFields = nameof(Id),
            ErrorMessage = "A tour with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Price per person is required.")]
        [Range(0, 9_999_999, ErrorMessage = "Price must be zero or more.")]
        [Display(Name = "Price / person")]
        public decimal PricePerPerson { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
