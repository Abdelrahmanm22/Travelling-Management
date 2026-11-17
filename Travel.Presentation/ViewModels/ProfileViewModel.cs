using System.ComponentModel.DataAnnotations;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// The signed-in user's own account page. Two independent forms post to two actions, each bound
    /// with its own prefix (<c>Info.</c> / <c>Password.</c>) so one never validates the other.
    /// </summary>
    public class ProfileViewModel
    {
        public string FullName { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        /// <summary>The stored photo, not a preview — null means the gender default is showing.</summary>
        public string? ImageUrl { get; set; }

        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

        public ProfileInfoInput Info { get; set; } = new();

        public ChangePasswordInput Password { get; set; } = new();
    }

    /// <summary>
    /// Has no UserName or Email on purpose: login identifiers are an administrator's to change, and a
    /// property that doesn't exist can't be set by a tampered post.
    /// </summary>
    public class ProfileInfoInput
    {
        public const long MaxImageBytes = 2 * 1024 * 1024;

        [Required(ErrorMessage = "First name is required.")]
        [StringLength(100)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(100)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        public Gender Gender { get; set; } = Gender.Male;

        [Display(Name = "Profile photo")]
        public IFormFile? ProfileImage { get; set; }

        /// <summary>Set by the Remove button; ignored when a new photo is uploaded in the same save.</summary>
        public bool RemoveImage { get; set; }
    }

    public class ChangePasswordInput
    {
        [Required(ErrorMessage = "Enter your current password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a new password.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm the new password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
