using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Presentation.ViewModels
{
    public class DriverViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        [Remote(action: "IsNameAvailable", controller: "Driver", AdditionalFields = nameof(Id),
            ErrorMessage = "A driver with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
