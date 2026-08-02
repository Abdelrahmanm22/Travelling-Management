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
        public string Nationality { get; set; } = string.Empty;

        /// <summary>Hotel grade this booking is sold at — a package can be sold as either.</summary>
        public PackageCategory Category { get; set; }

        public string ArrivalFlightNo { get; set; } = string.Empty;
        public TimeOnly ArrivalTime { get; set; }
        public string DepartureFlightNo { get; set; } = string.Empty;
        public TimeOnly DepartureTime { get; set; }

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
