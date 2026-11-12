using Travel.Domain.Enums;

namespace Travel.Domain.Models
{
    /// <summary>
    /// One block of the company's standard contract text (Setup ▸ Contract Terms). Read live at
    /// generation time, so the contract page can never change the legal text.
    /// </summary>
    public class ContractTermsSection : BaseModel
    {
        public ContractTermsPlacement Placement { get; set; } = ContractTermsPlacement.Closing;
        public int SortOrder { get; set; }

        /// <summary>"CLÁUSULA SEGUNDA - OBRIGAÇÕES DA CONTRATADA"; null = a continuation block.</summary>
        public string? Heading { get; set; }

        /// <summary>May use {Contratante}, {CPF}, {RG}, {Endereco}.</summary>
        public string Body { get; set; } = string.Empty;
    }
}
