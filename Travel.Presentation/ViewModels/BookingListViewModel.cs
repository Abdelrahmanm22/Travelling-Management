using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;

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

        [Display(Name = "Status")]
        public BookingStatus? Status { get; set; }

        public IEnumerable<SelectListItem> PackageOptions { get; set; } = Enumerable.Empty<SelectListItem>();

        public IReadOnlyList<Booking> Bookings { get; set; } = new List<Booking>();

        /// <summary>True when at least one filter is active — distinguishes "no matches" from "no bookings yet".</summary>
        public bool HasFilters =>
            ArrivalFrom is not null || ArrivalTo is not null || PackageId is not null || Status is not null;
    }

    /// <summary>
    /// Booking/Details. The booking itself plus the arrival groups it belongs to, so the page can show
    /// why it is (or isn't) confirmed without the view querying for it.
    /// </summary>
    public class BookingDetailsViewModel
    {
        public Booking Booking { get; set; } = null!;

        public IReadOnlyList<BookingBlockLine> Blocks { get; set; } = new List<BookingBlockLine>();
    }
}
