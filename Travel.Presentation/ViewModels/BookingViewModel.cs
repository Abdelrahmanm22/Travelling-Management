using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    public class BookingViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Guest name is required.")]
        [StringLength(150)]
        [Display(Name = "Guest name")]
        public string GuestFullName { get; set; } = string.Empty;

        [EmailAddress]
        [Display(Name = "Email")]
        public string? GuestEmail { get; set; }

        [Display(Name = "Phone")]
        public string? GuestPhone { get; set; }

        public string? Nationality { get; set; }

        [Range(1, 50, ErrorMessage = "PAX must be between 1 and 50.")]
        public int Pax { get; set; } = 1;

        [Required(ErrorMessage = "Package is required.")]
        [Display(Name = "Package")]
        public int PackageId { get; set; }

        [Required(ErrorMessage = "Arrival date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Arrival date")]
        public DateOnly ArrivalDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Departure date")]
        public DateOnly DepartureDate { get; set; }

        [Display(Name = "Cruise embarks")]
        public WeekDays? EmbarkDay { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Draft;

        public string? Notes { get; set; }

        public bool IsManuallyEdited { get; set; }

        // Display-only — set by the generator / controller.
        public int ExtraNights { get; set; }
        public string? ExtensionReason { get; set; }

        public List<BookingStayInput> Stays { get; set; } = new();

        // Form context — populated by the controller, not model-bound.
        public bool PackageHasFixedTotal { get; set; }
        public bool PackageHasCruise { get; set; }
        public int? PackageTotalNights { get; set; }
        public int? PackageBaseDestinationId { get; set; }
        public IEnumerable<SelectListItem> PackageOptions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem> StayDestinationOptions { get; set; } = new List<SelectListItem>();
    }

    public class BookingStayInput
    {
        [Required(ErrorMessage = "Destination is required.")]
        public int DestinationId { get; set; }

        [DataType(DataType.Date)]
        public DateOnly CheckInDate { get; set; }

        [Range(1, 120, ErrorMessage = "Nights must be at least 1.")]
        public int Nights { get; set; } = 1;

        public int SortOrder { get; set; }

        [StringLength(200)]
        public string? HotelName { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public bool IsOverridden { get; set; }
    }
}
