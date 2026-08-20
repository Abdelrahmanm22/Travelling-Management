using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    public class ExpenseTypeViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(150)]
        [Remote(action: "IsNameAvailable", controller: "ExpenseType", AdditionalFields = nameof(Id),
            ErrorMessage = "An expense type with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Display(Name = "How it divides")]
        public ExpenseBasis Basis { get; set; } = ExpenseBasis.PerRoom;

        /// <summary>
        /// Meaningful only for a per-pax type. The controller forces both back to false when the basis
        /// is per room, so flipping a type to per pax later can't resurrect a setting nobody re-chose.
        /// </summary>
        [Display(Name = "Charge for children")]
        public bool IncludeChildren { get; set; }

        [Display(Name = "Charge for infants")]
        public bool IncludeInfants { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
