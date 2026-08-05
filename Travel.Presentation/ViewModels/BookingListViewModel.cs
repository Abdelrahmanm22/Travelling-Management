using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Models;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Filter state plus results for the Bookings list.</summary>
    public class BookingListViewModel
    {
        [Display(Name = "Arrival from")]
        [DataType(DataType.Date)]
        public DateOnly? ArrivalFrom { get; set; }

        [Display(Name = "Arrival to")]
        [DataType(DataType.Date)]
        public DateOnly? ArrivalTo { get; set; }

        [Display(Name = "Package")]
        public int? PackageId { get; set; }

        public IEnumerable<SelectListItem> PackageOptions { get; set; } = Enumerable.Empty<SelectListItem>();

        public IReadOnlyList<Booking> Bookings { get; set; } = new List<Booking>();

        /// <summary>True when at least one filter is active — distinguishes "no matches" from "no bookings yet".</summary>
        public bool HasFilters => ArrivalFrom is not null || ArrivalTo is not null || PackageId is not null;
    }
}
