using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Specifications;
using Travel.Presentation.Services.Vouchers;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>
    /// Renders a contract PDF from the edited document, reading the legal terms and settings live from
    /// Setup (decision 6) — so a terms edit reaches the next render of every draft.
    /// </summary>
    public sealed class ContractRenderer
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly VoucherAssets _assets;

        public ContractRenderer(IUnitOfWork unitOfWork, VoucherAssets assets)
        {
            _unitOfWork = unitOfWork;
            _assets = assets;
        }

        public async Task<byte[]> RenderAsync(string bookingCode, ContractDocument document, ContractPartyDetails party)
        {
            var (settings, terms) = await LoadTermsAsync(_unitOfWork);
            return BookingContractPdf.Render(new ContractPrintModel(bookingCode, document, party, settings, terms, _assets.LogoPng));
        }

        /// <summary>The settings row (or blank defaults) and every terms section in print order.</summary>
        public static async Task<(ContractSettings Settings, IReadOnlyList<ContractTermsSection> Terms)> LoadTermsAsync(IUnitOfWork unitOfWork)
        {
            var settings = (await unitOfWork.Repository<ContractSettings>().GetAllAsync(new BaseSpecifications<ContractSettings>()))
                .OrderBy(s => s.Id).FirstOrDefault() ?? new ContractSettings();

            var terms = (await unitOfWork.Repository<ContractTermsSection>().GetAllAsync(new BaseSpecifications<ContractTermsSection>()))
                .OrderBy(t => t.Placement).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
                .ToList();

            return (settings, terms);
        }
    }
}
