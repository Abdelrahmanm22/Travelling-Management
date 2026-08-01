using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum GuestType
    {
        [Display(Name = "Adult")]
        Adult = 1,

        [Display(Name = "Child (6-12)")]
        Child = 2,

        [Display(Name = "Infant (0-5)")]
        Infant = 3,
    }
}
