using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    public class DestinationViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        [Remote(action: "IsNameAvailable", controller: "Destination", AdditionalFields = nameof(Id),
            ErrorMessage = "A destination with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Cruise / sailing stay")]
        public bool IsCruise { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        /// <summary>Checked sailing days — combined into the <see cref="WeekDays"/> bitmask (cruise only).</summary>
        [Display(Name = "Departure days")]
        public List<WeekDays> SelectedDepartureDays { get; set; } = new();
    }
}
