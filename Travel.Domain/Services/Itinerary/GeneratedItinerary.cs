namespace Travel.Domain.Services.Itinerary
{
    /// <summary>Result of the accommodation-schedule generator — pure data, no persistence concerns.</summary>
    public class GeneratedItinerary
    {
        public List<GeneratedStay> Stays { get; set; } = new();

        /// <summary>Nights beyond the package's sold total (e.g. Saturday auto-extension). 0 when none.</summary>
        public int ExtraNights { get; set; }

        public string? ExtensionReason { get; set; }

        public DateOnly DepartureDate { get; set; }
    }

    public class GeneratedStay
    {
        public int DestinationId { get; set; }
        public bool IsCruise { get; set; }
        public DateOnly CheckIn { get; set; }
        public int Nights { get; set; }
        public int SortOrder { get; set; }

        /// <summary>
        /// The nights this stay would have had at the package's sold length. Equal to
        /// <see cref="Nights"/> for every stay except the base-return leg when the auto-extension
        /// fired — there it holds the un-extended figure, which is what makes the extension show up
        /// as chargeable extra nights on the row it actually landed on.
        /// </summary>
        public int BaselineNights { get; set; }
    }
}
