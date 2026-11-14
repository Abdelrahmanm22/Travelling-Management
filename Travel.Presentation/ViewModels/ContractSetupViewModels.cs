using System.ComponentModel.DataAnnotations;
using Travel.Domain.Enums;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Setup ▸ Contract Terms — the three settings and every terms section.</summary>
    public sealed class ContractTermsViewModel
    {
        [Required(ErrorMessage = "Enter the document title.")]
        [StringLength(200)]
        [Display(Name = "Document title")]
        public string DocumentTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the signature city.")]
        [StringLength(100)]
        [Display(Name = "Signature city")]
        public string SignatureCity { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the company's signature name.")]
        [StringLength(100)]
        [Display(Name = "Company signature name")]
        public string CompanySignatureName { get; set; } = string.Empty;

        public List<ContractTermsSectionInput> Sections { get; set; } = new();

        public bool CanEdit { get; set; }
    }

    public sealed class ContractTermsSectionInput
    {
        public ContractTermsPlacement Placement { get; set; } = ContractTermsPlacement.Closing;

        [StringLength(300)]
        public string? Heading { get; set; }

        [Required(ErrorMessage = "Enter the section's text, or remove the section.")]
        public string Body { get; set; } = string.Empty;
    }

    /// <summary>Setup ▸ Packages ▸ Contract program.</summary>
    public sealed class PackageContractViewModel
    {
        public int PackageId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;

        [StringLength(150)]
        [Display(Name = "Contract title")]
        public string? ContractTitle { get; set; }

        [Display(Name = "Importante")]
        public string? ImportantText { get; set; }

        [Display(Name = "Documentos Requeridos")]
        public string? DocumentsText { get; set; }

        public List<PackageContractLegInput> Legs { get; set; } = new();

        /// <summary>The trip's last day(s) — "Cairo / País de Origem".</summary>
        public List<PackageContractDayInput> FinalDays { get; set; } = new();

        public List<PackageContractListInput> Includes { get; set; } = new();
        public List<PackageContractListInput> Excludes { get; set; } = new();

        /// <summary>Index into <see cref="Excludes"/> of the visa line; -1 for none.</summary>
        public int VisaLineIndex { get; set; } = -1;

        public bool CanEdit { get; set; }
        public bool IsOutOfDate { get; set; }
    }

    public sealed class PackageContractLegInput
    {
        public int Leg { get; set; }
        public int DestinationId { get; set; }

        /// <summary>Display only — re-derived from the package on every render.</summary>
        public string Label { get; set; } = string.Empty;

        [Range(0, 120, ErrorMessage = "Nights must be between 0 and 120.")]
        public int WrittenForNights { get; set; }

        [StringLength(150)]
        public string? Heading { get; set; }

        public List<PackageContractDayInput> Days { get; set; } = new();
    }

    public sealed class PackageContractDayInput
    {
        [Required(ErrorMessage = "Enter the day's title.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Body { get; set; }

        public bool IsDepartureDay { get; set; }
    }

    public sealed class PackageContractListInput
    {
        [Required(ErrorMessage = "Enter the line, or remove it.")]
        [StringLength(500)]
        public string Text { get; set; } = string.Empty;
    }
}
