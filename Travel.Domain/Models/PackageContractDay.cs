namespace Travel.Domain.Models
{
    /// <summary>One "Dia NN – …" block of a package's contract program. The number is generated.</summary>
    public class PackageContractDay : BaseModel
    {
        /// <summary>The <see cref="Leg"/> value of the trip's last day ("Cairo / País de Origem").</summary>
        public const int FinalLeg = -1;

        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        /// <summary>A <see cref="PackageContractLeg.Leg"/> index, or <see cref="FinalLeg"/>.</summary>
        public int Leg { get; set; }

        public int SortOrder { get; set; }

        /// <summary>Without the number: "Chegada ao Cairo".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Plain multi-line text; blank lines separate paragraphs.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>The day the guest leaves this leg's destination. Inserted free days go before it.</summary>
        public bool IsDepartureDay { get; set; }
    }
}
