using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// A guest booking against a <see cref="Package"/>. Its per-stay schedule (<see cref="Stays"/>) is
    /// generated from the package + arrival date, persisted as an editable starting point, and may be
    /// overridden by the operator.
    /// </summary>
    public class Booking : BaseModel
    {
        public string GuestFullName { get; set; } = string.Empty;
        public string? GuestEmail { get; set; }
        public string? GuestPhone { get; set; }
        public string? Nationality { get; set; }

        /// <summary>Number of guests (PAX).</summary>
        public int Pax { get; set; } = 1;

        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        public DateOnly ArrivalDate { get; set; }

        /// <summary>Computed and stored: arrival + package total (+ <see cref="ExtraNights"/>), or operator-entered for variable packages.</summary>
        public DateOnly DepartureDate { get; set; }

        /// <summary>Chosen cruise embark weekday — only meaningful for cruise packages with a Mon/Tue arrival (else null).</summary>
        public WeekDays? EmbarkDay { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Draft;

        public string? Notes { get; set; }

        /// <summary>True once the operator has hand-edited the generated schedule; blocks silent regeneration.</summary>
        public bool IsManuallyEdited { get; set; }

        /// <summary>Nights this booking runs beyond the package's sold total (Saturday auto-extension or manual edits). Chargeable.</summary>
        public int ExtraNights { get; set; }

        public string? ExtensionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<BookingStay> Stays { get; set; } = new List<BookingStay>();
    }
}
