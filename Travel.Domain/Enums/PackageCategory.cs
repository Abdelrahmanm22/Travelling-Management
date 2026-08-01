using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    /// <summary>Hotel grade the package is sold at for a given booking.</summary>
    public enum PackageCategory
    {
        [Display(Name = "4 Stars")]
        FourStars = 4,

        [Display(Name = "5 Stars")]
        FiveStars = 5,
    }
}
