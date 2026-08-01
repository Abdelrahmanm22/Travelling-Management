using System.ComponentModel.DataAnnotations;

namespace Travel.Domain.Enums
{
    public enum RoomType
    {
        [Display(Name = "SGL - Single")]
        Single = 1,

        [Display(Name = "DBL - Double")]
        Double = 2,

        [Display(Name = "TRP - Triple")]
        Triple = 3,
    }
}
