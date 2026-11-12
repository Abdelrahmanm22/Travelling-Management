using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>One line of a package's "Pacote inclui" / "Não está incluso" list.</summary>
    public class PackageContractListItem : BaseModel
    {
        public int PackageId { get; set; }
        public Package Package { get; set; } = null!;

        public ContractListKind Kind { get; set; } = ContractListKind.Includes;
        public int SortOrder { get; set; }
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The visa line — at most one per package, only on Excludes. A booking with
        /// <see cref="Booking.VisaIncluded"/> moves it to Includes.
        /// </summary>
        public bool IsVisaLine { get; set; }
    }
}
