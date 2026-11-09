using Travel.Domain.Enums;

namespace Travel.Domain.Services.InternalMovements
{
    /// <summary>
    /// A booking's flight as read live from the booking (international) or from the arrival group's
    /// <c>BlockFlight</c> (domestic). Never stored on a movement.
    /// </summary>
    public sealed record ResolvedFlight(string? Number, TimeOnly? Time, DateOnly Date)
    {
        /// <summary>False when neither a number nor a time has been entered yet — "no flight recorded".</summary>
        public bool IsRecorded => !string.IsNullOrWhiteSpace(Number) || Time.HasValue;
    }

    /// <summary>A passive note on a booking card: this booking flies in or out of here today.</summary>
    public sealed record FlightHint(FlightLeg Leg, ResolvedFlight Flight);

    /// <summary>Where a booking card sits on the day board.</summary>
    public enum BoardSection
    {
        Arriving = 1,
        Departing = 2,
        Staying = 3,
        Elsewhere = 4,
    }

    /// <summary>
    /// The answer to "is this booking in this destination on this day, and how" — computed by
    /// <see cref="MovementPresence.Evaluate"/>.
    /// </summary>
    public sealed record Presence(bool Qualifies, bool Arriving, bool Departing, IReadOnlyList<FlightHint> Hints);

    /// <summary>One recorded movement with everything derived about it. The single row shape the board,
    /// the calendar and the report all read.</summary>
    public sealed class MovementLine
    {
        public int Id { get; init; }
        public DateOnly Date { get; init; }
        public TimeOnly PickupTime { get; init; }
        public int DestinationId { get; init; }
        public string DestinationName { get; init; } = string.Empty;

        public int BookingId { get; init; }
        public string BookingCode { get; init; } = string.Empty;
        public string LeadGuestName { get; init; } = string.Empty;
        public int BookingPax { get; init; }
        public PackageCategory Category { get; init; }

        public int Pax { get; init; }

        public int MovementTypeId { get; init; }
        public string MovementTypeName { get; init; } = string.Empty;
        public FlightLeg? FlightLeg { get; init; }

        public int DriverId { get; init; }
        public string DriverName { get; init; } = string.Empty;

        public string? Hotel { get; init; }

        /// <summary>Null for a non-flight type, or a domestic leg no <c>BlockFlight</c> covers.</summary>
        public ResolvedFlight? Flight { get; init; }

        /// <summary>Set when the movement no longer fits its booking — the reason in words.</summary>
        public string? ReviewReason { get; init; }

        public bool NeedsReview => ReviewReason is not null;

        /// <summary>An amber warning, not a review reason: a flight type whose flight isn't entered yet.</summary>
        public bool NoFlightRecorded => FlightLeg is not null && Flight is not { IsRecorded: true };

        public string? CreatedByName { get; init; }
        public string? UpdatedByName { get; init; }
    }

    /// <summary>A booking card on the day board.</summary>
    public sealed class BoardBooking
    {
        public int BookingId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string LeadGuestName { get; init; } = string.Empty;
        public int Pax { get; init; }
        public PackageCategory Category { get; init; }
        public bool IsPrivate { get; init; }
        public BoardSection Section { get; init; }
        public string? Hotel { get; init; }
        public IReadOnlyList<FlightHint> Hints { get; init; } = Array.Empty<FlightHint>();

        /// <summary>Each leg resolved for this booking at this destination and day, so the popup can show
        /// the right flight for whichever type is dropped without another round trip.</summary>
        public IReadOnlyDictionary<FlightLeg, ResolvedFlight?> Flights { get; init; } =
            new Dictionary<FlightLeg, ResolvedFlight?>();

        public IReadOnlyList<MovementLine> Movements { get; init; } = Array.Empty<MovementLine>();

        /// <summary>For an Elsewhere card: where the booking is on this day.</summary>
        public string? ElsewhereLabel { get; init; }
    }

    public sealed class DayBoard
    {
        public int DestinationId { get; init; }
        public string DestinationName { get; init; } = string.Empty;
        public DateOnly Date { get; init; }

        /// <summary>Every movement on this day that needs review, whether or not its booking is still here.</summary>
        public IReadOnlyList<MovementLine> NeedsReview { get; init; } = Array.Empty<MovementLine>();
        public IReadOnlyList<BoardBooking> Arriving { get; init; } = Array.Empty<BoardBooking>();
        public IReadOnlyList<BoardBooking> Departing { get; init; } = Array.Empty<BoardBooking>();
        public IReadOnlyList<BoardBooking> Staying { get; init; } = Array.Empty<BoardBooking>();
        public IReadOnlyList<BoardBooking> Elsewhere { get; init; } = Array.Empty<BoardBooking>();

        public int MovementCount { get; init; }
    }

    public sealed record CalendarDay(DateOnly Date, int Bookings, int Movements, int NeedsReview);

    /// <summary>A booking that may receive a movement on the requested day, with the facts the save needs.</summary>
    public sealed record QualifiedBooking(int BookingId, string Code, int Pax);

    /// <summary>The outcome of checking a set of bookings for a save.</summary>
    public sealed record QualificationResult(
        IReadOnlyDictionary<int, QualifiedBooking> Qualified,
        IReadOnlyList<string> RefusedCodes,
        IReadOnlyList<int> MissingIds);
}
