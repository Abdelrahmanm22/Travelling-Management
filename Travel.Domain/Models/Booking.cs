using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// A guest booking against a <see cref="Package"/>. Its per-stay schedule (<see cref="Stays"/>) is
    /// generated from the package + arrival date, persisted as an editable starting point, and may be
    /// overridden by the operator.
    /// </summary>
    public class Booking : BaseModel, ICodedEntity
    {
        /// <summary>Auto-generated sequential reference, e.g. "MUSB-000001". Issued on create, never changed.</summary>
        public string Code { get; set; } = string.Empty;

        public string GuestEmail { get; set; } = string.Empty;
        public string GuestPhone { get; set; } = string.Empty;
        /// <summary>
        /// Nullable: bookings taken before nationalities became a lookup carry no value, and the
        /// operator fills one in the next time the booking is edited (the form requires it).
        /// </summary>
        public int? NationalityId { get; set; }
        public Nationality? Nationality { get; set; }

        /// <summary>Hotel grade this booking is sold at — a package can be sold as either.</summary>
        public PackageCategory Category { get; set; }

        /// <summary>
        /// Lifecycle. InProgress/Confirmed are derived by Block Services — see
        /// <see cref="Enums.BookingStatus"/> — so they are never set from the Create/Edit form
        /// (the AutoMapper profile ignores this property on the ViewModel → entity map).
        /// </summary>
        public BookingStatus Status { get; set; } = BookingStatus.InProgress;

        // All optional — flight details are often unknown when the booking is first taken.
        // The times are nullable rather than defaulted: a blank form field mapping onto a
        // non-nullable TimeOnly would silently store (and display) a real-looking 00:00.
        public string? ArrivalFlightNo { get; set; }
        public TimeOnly? ArrivalTime { get; set; }
        public string? DepartureFlightNo { get; set; }
        public TimeOnly? DepartureTime { get; set; }

        /// <summary>
        /// When the guest actually flies in/out — display only, and deliberately NOT
        /// <see cref="ArrivalDate"/>/<see cref="DepartureDate"/>, which are the package start/end
        /// and drive the itinerary generator and Block Services. Guests routinely fly in the night
        /// before and out after. Null means "same day as the package date" and renders as a fallback.
        /// </summary>
        public DateOnly? FlightArrivalDate { get; set; }
        public DateOnly? FlightDepartureDate { get; set; }

        public int Adults { get; set; } = 1;
        public int Children { get; set; }
        public int Infants { get; set; }

        /// <summary>Total travellers (PAX) — derived from the breakdown, not stored.</summary>
        public int Pax => Adults + Children + Infants;

        /// <summary>The lead guest's name — the booking's display name across the app.</summary>
        public string LeadGuestName =>
            Guests.FirstOrDefault(g => g.IsLeadGuest)?.FullName
            ?? Guests.OrderBy(g => g.SortOrder).FirstOrDefault()?.FullName
            ?? "—";

        /// <summary>
        /// A domestic leg is implied by a cruise stay or an itinerary touching more than one destination.
        /// Like <see cref="Pax"/>/<see cref="LeadGuestName"/> this reads a loaded navigation, so EF cannot
        /// translate it and it reads false whenever <see cref="Stays"/> wasn't included — and it must be
        /// evaluated against the booking's WHOLE itinerary, never a date- or destination-filtered subset.
        /// </summary>
        public bool RequiresDomesticFlight =>
            Stays.Any(s => s.IsCruise) || Stays.Select(s => s.DestinationId).Distinct().Count() > 1;

        /// <summary>Requires a domestic flight but no document has been attached for it yet.</summary>
        public bool MissingDomesticFlightDoc =>
            RequiresDomesticFlight
            && !Attachments.Any(a => a.Category == AttachmentCategory.DomesticFlight);

        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        public DateOnly ArrivalDate { get; set; }

        /// <summary>Computed and stored: arrival + package total (+ <see cref="ExtraNights"/>), or operator-entered for variable packages.</summary>
        public DateOnly DepartureDate { get; set; }

        /// <summary>Chosen cruise embark weekday — only meaningful for cruise packages with a Mon/Tue arrival (else null).</summary>
        public WeekDays? EmbarkDay { get; set; }

        public string? Notes { get; set; }

        /// <summary>True once the operator has hand-edited the generated schedule; blocks silent regeneration.</summary>
        public bool IsManuallyEdited { get; set; }

        /// <summary>Nights this booking runs beyond the package's sold total (Saturday auto-extension or manual edits). Chargeable.</summary>
        public int ExtraNights { get; set; }

        public string? ExtensionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<BookingStay> Stays { get; set; } = new List<BookingStay>();

        public ICollection<BookingGuest> Guests { get; set; } = new List<BookingGuest>();

        public ICollection<BookingRoom> Rooms { get; set; } = new List<BookingRoom>();

        public ICollection<BookingAttachment> Attachments { get; set; } = new List<BookingAttachment>();
    }
}
