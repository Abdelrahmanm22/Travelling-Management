namespace Travel.Domain.Models
{
    /// <summary>
    /// The single row of contract-wide settings (Setup ▸ Contract Terms). Its existence is also what
    /// tells the startup seed the terms were seeded once — deleting every section never re-seeds.
    /// </summary>
    public class ContractSettings : BaseModel
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string SignatureCity { get; set; } = string.Empty;
        public string CompanySignatureName { get; set; } = string.Empty;
    }
}
