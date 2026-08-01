using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    public class BookingViewModel
    {
        public int Id { get; set; }

        // Display only — issued by the code generator on create and ignored on the way back to the entity.
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        [StringLength(150)]
        [Display(Name = "Email")]
        public string GuestEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone is required.")]
        [StringLength(30)]
        [Display(Name = "Phone")]
        public string GuestPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nationality is required.")]
        [StringLength(100)]
        public string Nationality { get; set; } = string.Empty;

        // Nullable + [Required] on purpose: a non-nullable enum with an empty <select> yields the raw
        // binder message ("The value '' is invalid") instead of a friendly one.
        [Required(ErrorMessage = "Package category is required.")]
        [Display(Name = "Package category")]
        public PackageCategory? Category { get; set; }

        [Required(ErrorMessage = "Arrival flight number is required.")]
        [StringLength(20)]
        [Display(Name = "Arrival flight no.")]
        public string ArrivalFlightNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Arrival time is required.")]
        [DataType(DataType.Time)]
        [Display(Name = "Arrival time")]
        public TimeOnly? ArrivalTime { get; set; }

        [Required(ErrorMessage = "Departure flight number is required.")]
        [StringLength(20)]
        [Display(Name = "Departure flight no.")]
        public string DepartureFlightNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Departure time is required.")]
        [DataType(DataType.Time)]
        [Display(Name = "Departure time")]
        public TimeOnly? DepartureTime { get; set; }

        [Range(1, 50, ErrorMessage = "A booking needs at least one adult.")]
        [Display(Name = "Adults")]
        public int Adults { get; set; } = 1;

        [Range(0, 50, ErrorMessage = "Children must be between 0 and 50.")]
        [Display(Name = "Children (6-12)")]
        public int Children { get; set; }

        [Range(0, 50, ErrorMessage = "Infants must be between 0 and 50.")]
        [Display(Name = "Infants (0-5)")]
        public int Infants { get; set; }

        public int TotalPax => Adults + Children + Infants;

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

        public string? Notes { get; set; }

        public bool IsManuallyEdited { get; set; }

        // Display-only — set by the generator / controller.
        public int ExtraNights { get; set; }
        public string? ExtensionReason { get; set; }

        public List<BookingStayInput> Stays { get; set; } = new();

        public List<BookingGuestInput> Guests { get; set; } = new();

        public List<BookingRoomInput> Rooms { get; set; } = new();

        /// <summary>Index into <see cref="Guests"/> of the lead guest — bound from a single radio group.</summary>
        [Display(Name = "Lead guest")]
        public int LeadGuestIndex { get; set; }

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

    public class BookingGuestInput
    {
        [Required(ErrorMessage = "Guest name is required.")]
        [StringLength(150)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        public GuestType Type { get; set; } = GuestType.Adult;

        /// <summary>Range depends on Type — cross-field validated in the controller.</summary>
        public int? Age { get; set; }

        public int SortOrder { get; set; }
    }

    public class BookingRoomInput
    {
        public RoomType Type { get; set; } = RoomType.Double;

        [Range(1, 50, ErrorMessage = "Room quantity must be at least 1.")]
        public int Quantity { get; set; } = 1;

        public int SortOrder { get; set; }
    }
}
