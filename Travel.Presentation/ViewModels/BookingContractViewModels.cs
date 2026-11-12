using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Presentation.Services.Contracts;

namespace Travel.Presentation.ViewModels
{
    /// <summary>The contract page — <c>BookingContract/Edit/{bookingId}</c>.</summary>
    public sealed class BookingContractEditViewModel
    {
        public int BookingId { get; init; }
        public string BookingCode { get; init; } = string.Empty;
        public int PackageId { get; init; }
        public string PackageName { get; init; } = string.Empty;
        public BookingStatus Status { get; init; }
        public bool IsPriced { get; init; }

        public required ContractDocument Document { get; init; }
        public required ContractBasis Basis { get; init; }
        public IReadOnlyList<ContractWarning> Warnings { get; init; } = Array.Empty<ContractWarning>();

        /// <summary>Sections the booking changed since the draft was saved (spec §6.4).</summary>
        public IReadOnlyList<string> StaleSections { get; init; } = Array.Empty<string>();

        public DateTime? DraftUpdatedAt { get; init; }
        public DateTime? GeneratedAt { get; init; }
        public string? GeneratedByName { get; init; }
        public bool HasGeneratedPdf { get; init; }

        public IReadOnlyList<ContractTermsSection> Terms { get; init; } = Array.Empty<ContractTermsSection>();

        public bool CanEditTerms { get; init; }
        public bool CanEditPackage { get; init; }
        public bool CanEditPricing { get; init; }

        public bool IsCancelled => Status == BookingStatus.Cancelled;
    }

    /// <summary>The JSON body of every contract-page POST.</summary>
    public sealed class ContractRequest
    {
        public int BookingId { get; set; }
        public ContractDocument? Document { get; set; }
        public ContractBasis? Basis { get; set; }

        /// <summary>CPF / RG / address. Used for the render and dropped — never persisted or logged.</summary>
        public ContractPartyDetails? Party { get; set; }
    }

    /// <summary>Refresh one booking-driven section from a fresh build.</summary>
    public sealed class ContractSectionRequest
    {
        public int BookingId { get; set; }
        public string Section { get; set; } = string.Empty;
    }
}
