using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    public class UserFormViewModel
    {
        public string? Id { get; set; }

        public bool IsCreate => string.IsNullOrEmpty(Id);

        [Required(ErrorMessage = "First name is required.")]
        [StringLength(100)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(100)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50)]
        [Display(Name = "Username")]
        [Remote(action: "IsUserNameAvailable", controller: "User", AdditionalFields = nameof(Id),
            ErrorMessage = "This username is already taken.")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email address")]
        [Remote(action: "IsEmailAvailable", controller: "User", AdditionalFields = nameof(Id),
            ErrorMessage = "This email is already registered.")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        public Gender Gender { get; set; } = Gender.Male;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        // Required on create only — enforced in the Create POST so the same view model serves Edit.
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
        public string? ConfirmPassword { get; set; }

        [Display(Name = "Profile photo")]
        public IFormFile? ProfileImage { get; set; }

        public string? ExistingImageUrl { get; set; }

        public List<string> SelectedRoles { get; set; } = new();

        public List<RoleOptionViewModel> AvailableRoles { get; set; } = new();
    }

    public class RoleOptionViewModel
    {
        public string Name { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public bool IsSelected { get; set; }

        public bool IsProtected { get; set; }

        public string ElementId => "role-" + Name.Replace(' ', '-');
    }

    public class UserPermissionsViewModel
    {
        public string UserId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public List<string> Roles { get; set; } = new();

        /// <summary>Direct extras only — inherited items render disabled and never post.</summary>
        public List<string> SelectedPermissions { get; set; } = new();

        public List<PermissionGroupViewModel> Groups { get; set; } = new();

        public int InheritedCount { get; set; }

        public int DirectCount { get; set; }
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
