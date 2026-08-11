using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Presentation.ViewModels
{
    public class NationalityViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100)]
        [Remote(action: "IsNameAvailable", controller: "Nationality", AdditionalFields = nameof(Id),
            ErrorMessage = "A nationality with this name already exists.")]
        public string Name { get; set; } = string.Empty;
    }
}
