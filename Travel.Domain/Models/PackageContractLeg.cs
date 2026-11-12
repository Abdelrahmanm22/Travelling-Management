namespace Travel.Domain.Models
{
    /// <summary>
    /// One leg of a package's contract program — Lead (0), a segment (1…n) or Return (n + 1) — and how
    /// many nights its days were written for. Free days are inserted when a booking's stay is longer.
    /// </summary>
    public class PackageContractLeg : BaseModel
    {
        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        /// <summary>0 = Lead, 1…n = <see cref="Package.Segments"/> by SortOrder, n + 1 = Return.</summary>
        public int Leg { get; set; }

        /// <summary>
        /// The leg's destination when the program was saved. Leg indexes follow the package's segments,
        /// so a later segment edit shifts them; this snapshot is how the mismatch is noticed and warned.
        /// </summary>
        public int DestinationId { get; set; }

        public int WrittenForNights { get; set; }

        /// <summary>"Programação Cruzeiro". Null → "Programação {Destination}".</summary>
        public string? Heading { get; set; }
    }
}
