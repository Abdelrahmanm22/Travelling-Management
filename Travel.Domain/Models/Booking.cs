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

        // Sale price per person for each pax type, in the app currency (Travel.Presentation/Helpers/Money.cs).
        // Nullable because bookings taken before pricing existed carry none — the form requires a price for
        // every type with a count, but the column has to let those rows be.
        public decimal? AdultPrice { get; set; }
        public decimal? ChildPrice { get; set; }
        public decimal? InfantPrice { get; set; }

        /// <summary>Total travellers (PAX) — derived from the breakdown, not stored.</summary>
        public int Pax => Adults + Children + Infants;

        /// <summary>True once every pax type that has a count also carries a price.</summary>
        public bool IsPriced =>
            (Adults == 0 || AdultPrice.HasValue) &&
            (Children == 0 || ChildPrice.HasValue) &&
            (Infants == 0 || InfantPrice.HasValue);

        /// <summary>
        /// The pax component of the sale price. Null — not 0 — while the booking is unpriced, so a
        /// complimentary booking and one nobody has priced yet never render the same.
        /// </summary>
        public decimal? PaxTotal => IsPriced
            ? Adults * (AdultPrice ?? 0m) + Children * (ChildPrice ?? 0m) + Infants * (InfantPrice ?? 0m)
            : null;

        /// <summary>Chargeable nights beyond the generated schedule, priced per stay.</summary>
        public decimal ExtraNightsTotal => Stays.Sum(s => s.ExtraNightsTotal);

        public decimal ToursTotal => Tours.Sum(t => t.Total);

        /// <summary>
        /// What the client owes: pax + extra nights + tours. Null while unpriced — a partial figure
        /// would be read as the price and quoted. Computed like <see cref="Pax"/>, so EF cannot
        /// translate it: never use it in a specification's Criteria, and it reads 0 for the components
        /// whose navigation wasn't included.
        /// </summary>
        public decimal? GrandTotal => PaxTotal is { } pax ? pax + ExtraNightsTotal + ToursTotal : null;

        /// <summary>Payments less refunds. Always a real figure — an unpriced booking can still be paid.</summary>
        public decimal PaidTotal => Transactions.Sum(t => t.SignedAmount);

        /// <summary>Outstanding. Null while there is no total to owe against.</summary>
        public decimal? Balance => GrandTotal is { } total ? total - PaidTotal : null;

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

        public ICollection<BookingTour> Tours { get; set; } = new List<BookingTour>();

        public ICollection<BookingTransaction> Transactions { get; set; } = new List<BookingTransaction>();

        public ICollection<BookingAuditEvent> AuditEvents { get; set; } = new List<BookingAuditEvent>();
    }
}
