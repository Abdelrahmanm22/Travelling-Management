using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;
using Travel.Domain.Services.Itinerary;
using Microsoft.AspNetCore.Http;

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

        // Nullable + [Required] for the same reason as Category: an empty <select> on a non-nullable
        // int yields the raw binder message instead of a friendly one. The column itself is nullable —
        // bookings taken before nationalities became a lookup have no value until they're next edited.
        [Required(ErrorMessage = "Nationality is required.")]
        [Display(Name = "Nationality")]
        public int? NationalityId { get; set; }

        // Nullable + [Required] on purpose: a non-nullable enum with an empty <select> yields the raw
        // binder message ("The value '' is invalid") instead of a friendly one.
        [Required(ErrorMessage = "Package category is required.")]
        [Display(Name = "Package category")]
        public PackageCategory? Category { get; set; }

        [Display(Name = "Private booking")]
        public bool IsPrivate { get; set; }

        // Every flight field is optional — the details often aren't known when the booking is taken.
        [StringLength(20)]
        [Display(Name = "Arrival flight no.")]
        public string? ArrivalFlightNo { get; set; }

        [DataType(DataType.Time)]
        [Display(Name = "Arrival time")]
        public TimeOnly? ArrivalTime { get; set; }

        [StringLength(20)]
        [Display(Name = "Departure flight no.")]
        public string? DepartureFlightNo { get; set; }

        [DataType(DataType.Time)]
        [Display(Name = "Departure time")]
        public TimeOnly? DepartureTime { get; set; }

        // When left empty the display falls back to the package date.
        [DataType(DataType.Date)]
        [Display(Name = "Flight arrival date")]
        public DateOnly? FlightArrivalDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Flight departure date")]
        public DateOnly? FlightDepartureDate { get; set; }

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

        // Sale price per person. No [Required]: a type with a count of 0 must be allowed to stay blank,
        // and the rule is skipped entirely for users without Bookings.EditPricing — so "required when
        // the count is > 0" is cross-field and lives in BookingController.ValidatePricing.
        [Range(0, 9_999_999, ErrorMessage = "Price must be zero or more.")]
        [Display(Name = "Price / adult")]
        public decimal? AdultPrice { get; set; }

        [Range(0, 9_999_999, ErrorMessage = "Price must be zero or more.")]
        [Display(Name = "Price / child")]
        public decimal? ChildPrice { get; set; }

        [Range(0, 9_999_999, ErrorMessage = "Price must be zero or more.")]
        [Display(Name = "Price / infant")]
        public decimal? InfantPrice { get; set; }

        public decimal? AdultTotal => AdultPrice is { } p && Adults > 0 ? Adults * p : null;
        public decimal? ChildTotal => ChildPrice is { } p && Children > 0 ? Children * p : null;
        public decimal? InfantTotal => InfantPrice is { } p && Infants > 0 ? Infants * p : null;

        /// <summary>Sale total, or null while any type with a count is still unpriced.</summary>
        public decimal? GrandTotal =>
            (Adults == 0 || AdultPrice.HasValue) &&
            (Children == 0 || ChildPrice.HasValue) &&
            (Infants == 0 || InfantPrice.HasValue)
                ? (AdultTotal ?? 0m) + (ChildTotal ?? 0m) + (InfantTotal ?? 0m)
                : null;

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

        public List<BookingTourInput> Tours { get; set; } = new();

        /// <summary>Index into <see cref="Guests"/> of the lead guest — bound from a single radio group.</summary>
        [Display(Name = "Lead guest")]
        public int LeadGuestIndex { get; set; }

        // ── First payment (Create only) ──────────────────────────────────────
        // No [Required]: the rule depends on two permissions and on the booking having a total at all,
        // so it lives in BookingController.ValidateFirstPayment rather than in an attribute.
        [Range(0, 9_999_999, ErrorMessage = "Amount must be zero or more.")]
        [Display(Name = "Amount")]
        public decimal? FirstPaymentAmount { get; set; }

        [Display(Name = "Method")]
        public PaymentMethod FirstPaymentMethod { get; set; } = PaymentMethod.Cash;

        [DataType(DataType.Date)]
        [Display(Name = "Paid on")]
        public DateOnly FirstPaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(100)]
        [Display(Name = "Reference")]
        public string? FirstPaymentReference { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? FirstPaymentNotes { get; set; }

        public List<IFormFile> FirstPaymentFiles { get; set; } = new();

        /// <summary>Transactions.Create — drives the first-payment card and the rule behind it.</summary>
        public bool CanCreateTransactions { get; set; }

        public List<IFormFile> FlightFiles { get; set; } = new();

        public List<IFormFile> PassportFiles { get; set; } = new();

        [Url(ErrorMessage = "Enter a valid URL.")]
        [StringLength(500)]
        [Display(Name = "Contract link")]
        public string? ContractLink { get; set; }

        // Checkbox list of attachment ids to remove on Edit — never use asp-for (see class-level convention note).
        public List<int> RemoveAttachmentIds { get; set; } = new();

        // Form context — populated by the controller, not model-bound.
        public bool CanEditPricing { get; set; }
        public bool CanViewPricing { get; set; }
        public bool PackageHasFixedTotal { get; set; }
        public bool PackageHasCruise { get; set; }
        public int? PackageTotalNights { get; set; }
        public int? PackageBaseDestinationId { get; set; }
        public IEnumerable<SelectListItem> PackageOptions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem> StayDestinationOptions { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem> NationalityOptions { get; set; } = new List<SelectListItem>();
        public IReadOnlyList<TourOptionViewModel> TourOptions { get; set; } = new List<TourOptionViewModel>();
        public List<EmbarkOptionViewModel> EmbarkOptions { get; set; } = new();
        public List<BookingAttachmentViewModel> ExistingAttachments { get; set; } = new();
    }

    /// <summary>
    /// One cruise sailing the operator can pick. Mirrors the JSON shape <c>Preview</c> returns, because
    /// the same DOM is produced server-side (_BookingEmbarkOptions) and client-side (renderEmbark) —
    /// build the label through <see cref="Build"/> so the two can't drift.
    /// </summary>
    public class EmbarkOptionViewModel
    {
        // The WeekDays enum name — what the EmbarkDay radios bind from.
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int ExtraNights { get; set; }

        public static List<EmbarkOptionViewModel> Build(
            IReadOnlyList<EmbarkOption> options, string baseDestinationName) =>
            options.Select(o => new EmbarkOptionViewModel
            {
                Value = o.Day.ToString(),
                Text = $"{o.Date:ddd dd MMM} — {o.LeadNights} night{(o.LeadNights == 1 ? "" : "s")} in {baseDestinationName} first",
                ExtraNights = o.ExtraNights,
            }).ToList();
    }

    /// <summary>Display-only projection of an existing <see cref="Travel.Domain.Models.BookingAttachment"/>.</summary>
    public class BookingAttachmentViewModel
    {
        public int Id { get; set; }
        public AttachmentCategory Category { get; set; }
        public string? FileUrl { get; set; }
        public string? OriginalFileName { get; set; }
        public string? LinkUrl { get; set; }
    }

    public class BookingStayInput
    {
        [Required(ErrorMessage = "Destination is required.")]
        public int DestinationId { get; set; }

        [DataType(DataType.Date)]
        public DateOnly CheckInDate { get; set; }

        [Range(1, 120, ErrorMessage = "Nights must be at least 1.")]
        public int Nights { get; set; } = 1;

        /// <summary>Posted back unchanged as a hidden field, like <see cref="IsOverridden"/>. A row the
        /// operator adds posts 0, so all of its nights are chargeable.</summary>
        public int BaselineNights { get; set; }

        /// <summary>
        /// Always exactly one entry per <see cref="RoomType"/>, in enum order — see
        /// <c>BookingController.NormalizeStayRates</c>. Razor indexes these positionally, and MVC's
        /// collection binder stops at the first missing index, so the set must never be partial.
        /// </summary>
        public List<BookingStayRateInput> Rates { get; set; } = new();

        [Display(Name = "Arranged by the guest")]
        public bool ExtraNightsArrangedByGuest { get; set; }

        public int ExtraNights => Math.Max(0, Nights - BaselineNights);

        public decimal ExtraNightPerNight => Rates.Sum(r => r.Quantity * (r.Rate ?? 0m));

        public bool ExtraNightsArePriced =>
            ExtraNights == 0 || ExtraNightsArrangedByGuest || Rates.Any(r => r.Quantity > 0 && r.Rate.HasValue);

        public decimal ExtraNightsTotal =>
            ExtraNightsArrangedByGuest ? 0m : ExtraNights * ExtraNightPerNight;

        public int SortOrder { get; set; }

        [StringLength(200)]
        public string? HotelName { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public bool IsOverridden { get; set; }
    }

    /// <summary>
    /// One room type's extra-night rate on a stay row. <see cref="Rate"/> is nullable because 0 is a
    /// legal rate (a comped room) and only ABSENT is an error — the same distinction
    /// <c>ExpenseRateInputViewModel</c> draws.
    /// </summary>
    public class BookingStayRateInput
    {
        public RoomType RoomType { get; set; }

        [Range(0, 999, ErrorMessage = "Rooms must be zero or more.")]
        public int Quantity { get; set; }

        [Range(0, 9_999_999, ErrorMessage = "Rate must be zero or more.")]
        public decimal? Rate { get; set; }
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

    public class BookingTourInput
    {
        [Range(1, int.MaxValue, ErrorMessage = "Select a tour.")]
        public int TourId { get; set; }

        /// <summary>
        /// Display only — never posted. The saved figure is the count of <see cref="GuestIndexes"/>.
        /// </summary>
        public int Pax { get; set; }

        /// <summary>
        /// Positions in <see cref="BookingViewModel.Guests"/> of the guests taking this tour, bound from
        /// repeated checkboxes. Never <c>asp-for</c> — its companion hidden input corrupts the list.
        /// </summary>
        public List<int> GuestIndexes { get; set; } = new();

        /// <summary>
        /// The stored pax of a tour saved before names were recorded (null otherwise), so the form can
        /// say what it was sold as while asking for the names.
        /// </summary>
        public int? LegacyPax { get; set; }

        /// <summary>Defaulted from the catalogue in the form, then stored as sold.</summary>
        [Range(0, 9_999_999, ErrorMessage = "Price must be zero or more.")]
        public decimal UnitPrice { get; set; }

        public int SortOrder { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// One pickable tour. Carries the catalogue rate so the form can pre-fill the price box without a
    /// round-trip — <see cref="Microsoft.AspNetCore.Mvc.Rendering.SelectListItem"/> has nowhere to put it.
    /// </summary>
    public class TourOptionViewModel
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}
